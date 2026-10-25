using System.Net;

namespace Yukti.Application.Security;

/// <summary>
/// D6-T5: shared SSRF guard. One decision point — "which resolved IP address
/// is this (host, tenant) allowed to connect to" — consumed by both the API
/// module's HTTP client and the Web module's browser proxy (Volume: Day 6
/// spec, "one implementation, two consumers"). The check happens here, on an
/// already-resolved IPAddress, never on the URL/host string, so it holds
/// against DNS rebinding and redirects: each new connection re-enters this
/// method with a fresh resolution.
/// </summary>
public interface IOutboundGuard
{
    /// <summary>
    /// Resolves <paramref name="host"/> and returns every address the caller
    /// may connect to, in resolution order — never the full candidate list
    /// unfiltered: only the ones this guard actually allows. Throws
    /// <see cref="BlockedTargetException"/> only when EVERY resolved address
    /// is denied and the (host, tenant) pair isn't allow-listed. The caller
    /// must connect to one of the returned addresses directly, trying each
    /// in order on a connection failure before giving up — never re-resolve
    /// the host name, or the whole point of this method is defeated.
    ///
    /// Plural, not singular, by design: a real regression, caught live
    /// while verifying D6-T7's quickstart doc, is why. "localhost" resolves
    /// dual-stack on most machines (confirmed directly: `::1` before
    /// `127.0.0.1`), and a target listening on IPv4 only — not unusual —
    /// would have been unreachable forever if a single-address version of
    /// this method picked the IPv6 candidate and the caller never got a
    /// chance to fall back, something a plain SocketsHttpHandler with no
    /// custom ConnectCallback has always done for free.
    /// </summary>
    Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(string host, Guid? tenantId, CancellationToken ct);
}

public enum OutboundMode { Permissive, Restricted }

/// <summary>
/// Raised by <see cref="IOutboundGuard"/> when a target resolves to a
/// network address this guard's mode/allow-list doesn't permit. The message
/// is the exact user-facing wording the Day 6 spec specifies.
/// </summary>
public sealed class BlockedTargetException : Exception
{
    public string Host { get; }
    public IPAddress Address { get; }

    public BlockedTargetException(string host, IPAddress address)
        : base("Blocked: this target resolves to a private network address. Add it to the allow-list if this is intended.")
    {
        Host = host;
        Address = address;
    }
}

/// <summary>
/// The guard's own default when nothing else is wired up — every call site
/// that constructs ApiModule/WebModule without dependency injection (the
/// CLI, Yukti.Host, the eval host, and every existing test) gets this, so
/// none of them regress: it resolves and returns the first address with no
/// deny-list check at all, exactly today's pre-D6-T5 behavior. Only
/// Yukti.Api's own DI container registers the real, config-driven guard
/// (Yukti.Infrastructure's ConfiguredOutboundGuard) in its place — the one
/// process that is ever actually "hosted" in the sense this guard exists for.
/// </summary>
public sealed class PermissiveOutboundGuard : IOutboundGuard
{
    public static readonly PermissiveOutboundGuard Instance = new();
    private PermissiveOutboundGuard() { }

    public async Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(string host, Guid? tenantId, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
            return new[] { literal };

        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0)
            throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);

        // Permissive means no deny-list filtering — every resolved address is a candidate,
        // in the order the resolver returned them, exactly like an unguarded HttpClient.
        return addresses;
    }
}
