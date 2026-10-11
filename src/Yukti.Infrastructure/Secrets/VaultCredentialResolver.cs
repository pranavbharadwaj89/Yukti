using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Yukti.Contracts;

namespace Yukti.Infrastructure.Secrets;

/// <summary>
/// Volume 1 Part III §18.4 / Volume 4 Part IV §14-15: the real credential
/// resolver — reads against a HashiCorp Vault KV v2 secrets engine instead
/// of the in-memory stand-in (<see cref="InMemoryCredentialResolver"/>,
/// kept as the local/offline fallback — see registration in Program.cs).
///
/// Each <paramref name="ResolveAsync"/> call is a single, scoped Vault read
/// for exactly the named reference — never a broader listing or enumeration
/// — preserving the same least-privilege guarantee the in-memory resolver's
/// own doc comment describes: a module can resolve only the specific
/// credential a flow step wired in, nothing else.
/// </summary>
public sealed class VaultCredentialResolver : ICredentialResolver
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VaultCredentialResolver> _logger;
    private readonly string _vaultAddress;
    private readonly string? _vaultToken;
    private readonly string _mountPath;
    private readonly string _basePath;
    private readonly string _valueField;

    public VaultCredentialResolver(IConfiguration config, ILogger<VaultCredentialResolver> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _vaultAddress = (config["Vault:Address"] ?? Environment.GetEnvironmentVariable("VAULT_ADDR") ?? "http://127.0.0.1:8200").TrimEnd('/');
        _vaultToken = config["Vault:Token"] ?? Environment.GetEnvironmentVariable("VAULT_TOKEN");
        _mountPath = config["Vault:MountPath"] ?? "secret";
        _basePath = (config["Vault:BasePath"] ?? "yukti/credentials").Trim('/');
        _valueField = config["Vault:ValueField"] ?? "value";
    }

    public async Task<string?> ResolveAsync(string credentialReference, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_vaultToken))
        {
            _logger.LogWarning(
                "Vault token not configured — credential reference {CredentialReference} could not be resolved",
                credentialReference);
            return null;
        }

        var path = $"{_vaultAddress}/v1/{_mountPath}/data/{_basePath}/{Uri.EscapeDataString(credentialReference)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Vault-Token", _vaultToken);

            using var response = await _httpClient.SendAsync(request, ct);

            // A credential that simply doesn't exist is an expected, non-exceptional
            // outcome — resolved to null exactly like InMemoryCredentialResolver's
            // Dictionary.GetValueOrDefault, never a thrown error.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var outer) ||
                outer.ValueKind != JsonValueKind.Object ||
                !outer.TryGetProperty("data", out var inner) ||
                inner.ValueKind != JsonValueKind.Object ||
                !inner.TryGetProperty(_valueField, out var valueElement) ||
                valueElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return valueElement.GetString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Credential resolution failing (Vault unreachable, sealed, malformed
            // response) must never crash a flow run — the step that needed the
            // credential fails cleanly through FlowEngine's own fail-fast step
            // dispatch instead of an unhandled exception escaping this boundary.
            _logger.LogError(ex, "Vault credential resolution failed for {CredentialReference}", credentialReference);
            return null;
        }
    }
}
