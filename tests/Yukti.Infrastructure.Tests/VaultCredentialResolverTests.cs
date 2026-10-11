using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yukti.Infrastructure.Secrets;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// FR-SEC live evidence against a real Vault dev server — same
/// environment-dependent, skip-if-unconfigured pattern as
/// FlowReportProjectionConsumerTests (real infrastructure, never mocked,
/// per Volume 1 Part VI §40.6; skip rather than fail when that
/// infrastructure isn't present in the current environment).
///
/// Run a local dev server to exercise these for real:
///   vault server -dev -dev-root-token-id=yukti-dev-root-token
/// </summary>
public sealed class VaultCredentialResolverTests
{
    private const string Address = "http://127.0.0.1:8200";
    private const string Token = "yukti-dev-root-token";

    private static async Task<bool> VaultIsReachableAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync($"{Address}/v1/sys/health");
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotImplemented; // NotImplemented = uninitialized, still "reachable"
        }
        catch
        {
            return false;
        }
    }

    private static async Task WriteSecretAsync(string reference, string value)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{Address}/v1/secret/data/yukti/credentials/{Uri.EscapeDataString(reference)}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { data = new Dictionary<string, string> { ["value"] = value } }),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Vault-Token", Token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static VaultCredentialResolver CreateResolver()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Address"] = Address,
                ["Vault:Token"] = Token,
            })
            .Build();
        return new VaultCredentialResolver(config, NullLogger<VaultCredentialResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_returns_the_real_secret_value_written_to_vault()
    {
        if (!await VaultIsReachableAsync()) return; // Skip: no live Vault dev server in this environment.

        var reference = $"test-credential-{Guid.NewGuid():N}";
        await WriteSecretAsync(reference, "hunter2-super-secret-value");

        var resolver = CreateResolver();
        var resolved = await resolver.ResolveAsync(reference, CancellationToken.None);

        Assert.Equal("hunter2-super-secret-value", resolved);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_a_credential_reference_that_was_never_written()
    {
        if (!await VaultIsReachableAsync()) return; // Skip: no live Vault dev server in this environment.

        var resolver = CreateResolver();
        var resolved = await resolver.ResolveAsync($"never-written-{Guid.NewGuid():N}", CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_rather_than_throwing_when_no_token_is_configured()
    {
        // No skip needed here — this path never touches the network at all,
        // so it's exercisable identically with or without a live Vault server.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vault:Address"] = Address })
            .Build();
        var resolver = new VaultCredentialResolver(config, NullLogger<VaultCredentialResolver>.Instance);

        var resolved = await resolver.ResolveAsync("anything", CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_rather_than_throwing_when_vault_is_unreachable()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Address"] = "http://127.0.0.1:1", // nothing listens here
                ["Vault:Token"] = "irrelevant-token",
            })
            .Build();
        var resolver = new VaultCredentialResolver(config, NullLogger<VaultCredentialResolver>.Instance);

        var resolved = await resolver.ResolveAsync("anything", CancellationToken.None);

        Assert.Null(resolved);
    }
}
