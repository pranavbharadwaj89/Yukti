using System.Net;
using Microsoft.Extensions.Configuration;
using Yukti.Application.Security;

namespace Yukti.Infrastructure.Security;

/// <summary>
/// D6-T5: the real, config-driven SSRF guard — registered only in
/// Yukti.Api's DI container (the one process that is ever actually
/// "hosted"). Every other construction site (CLI, Yukti.Host, eval host,
/// every pre-existing test) keeps using PermissiveOutboundGuard by default,
/// so none of them regress; this type is additive, never a replacement for
/// the default.
///
/// Config shape (plain IConfiguration indexers/GetChildren — no
/// Configuration.Binder package, same convention D5-T3 already set for this
/// repo):
///   Outbound:Mode = "Permissive" | "Restricted" (default: Restricted —
///     the shipped config is safe unless someone deliberately loosens it)
///   Outbound:AllowList:&lt;tenantId&gt;:0, :1, ... = hostname or CIDR,
///     per tenant, empty by default.
/// </summary>
public sealed class ConfiguredOutboundGuard : IOutboundGuard
{
    private static readonly IPNetwork[] DenyList =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"), // cloud metadata endpoint lives here
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
    ];

    private readonly IConfiguration _configuration;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public ConfiguredOutboundGuard(IConfiguration configuration)
        : this(configuration, static (host, ct) => Dns.GetHostAddressesAsync(host, ct))
    {
    }

    /// <summary>Test seam: a stub resolver proves deny/rebinding/redirect
    /// behavior deterministically, with no real DNS or public rebinding
    /// service involved (per the Day 6 spec's own instruction for this test
    /// set).</summary>
    public ConfiguredOutboundGuard(IConfiguration configuration, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        _configuration = configuration;
        _resolve = resolve;
    }

    public OutboundMode Mode =>
        string.Equals(_configuration["Outbound:Mode"], "Permissive", StringComparison.OrdinalIgnoreCase)
            ? OutboundMode.Permissive
            : OutboundMode.Restricted;

    public async Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(string host, Guid? tenantId, CancellationToken ct)
    {
        var candidates = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await _resolve(host, ct);

        if (candidates.Length == 0)
            throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);

        if (Mode == OutboundMode.Permissive)
            return candidates;

        var allowList = GetAllowList(tenantId);
        if (IsAllowListed(host, allowList))
            return candidates;

        // Keep every candidate that isn't denied, in resolution order — not just the first —
        // so a dual-stack host (localhost resolving ::1 then 127.0.0.1, say) still has a real
        // address to fall back to if the first one it tries refuses the connection. Only throw
        // when NOTHING resolved is allowed; naming the first denied address in that message.
        var allowed = new List<IPAddress>();
        IPAddress? firstDenied = null;
        foreach (var address in candidates)
        {
            if (IsDenied(address) && !IsAllowListed(address, allowList))
            {
                firstDenied ??= address;
                continue;
            }
            allowed.Add(address);
        }

        if (allowed.Count == 0)
            throw new BlockedTargetException(host, firstDenied!);

        return allowed;
    }

    private static bool IsDenied(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        foreach (var network in DenyList)
        {
            if (network.Contains(normalized))
                return true;
        }
        return false;
    }

    private IReadOnlyList<string> GetAllowList(Guid? tenantId)
    {
        if (tenantId is null)
            return [];

        var section = _configuration.GetSection($"Outbound:AllowList:{tenantId.Value}");
        return section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
    }

    private static bool IsAllowListed(string host, IReadOnlyList<string> allowList)
    {
        foreach (var entry in allowList)
        {
            if (string.Equals(entry, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsAllowListed(IPAddress address, IReadOnlyList<string> allowList)
    {
        foreach (var entry in allowList)
        {
            if (IPNetwork.TryParse(entry, out var network) && network.Contains(address))
                return true;
            if (IPAddress.TryParse(entry, out var literal) && literal.Equals(address))
                return true;
        }
        return false;
    }
}
