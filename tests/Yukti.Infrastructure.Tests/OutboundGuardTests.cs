using System.Net;
using Microsoft.Extensions.Configuration;
using Xunit;
using Yukti.Application.Security;
using Yukti.Domain.Execution;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Infrastructure.Security;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T5's adversarial set, per the Day 6 spec: each test is its own bypass
/// technique, not a single combined case, so a future regression names the
/// exact technique that broke. All run against a stub resolver, per the
/// spec's own instruction ("no public rebinding service needed") — the
/// resolved-IP decision is what's under test, not DNS itself.
/// </summary>
public sealed class OutboundGuardTests
{
    private static IConfiguration Config(string mode) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Outbound:Mode"] = mode }).Build();

    private static ConfiguredOutboundGuard Restricted(Func<string, CancellationToken, Task<IPAddress[]>> resolve) =>
        new(Config("Restricted"), resolve);

    private static Func<string, CancellationToken, Task<IPAddress[]>> MapTo(IPAddress address) =>
        (_, _) => Task.FromResult(new[] { address });

    [Fact]
    public async Task A_direct_private_IP_is_blocked()
    {
        var guard = Restricted(MapTo(IPAddress.Parse("10.0.0.1")));
        await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("10.0.0.1", null, default));
    }

    [Fact]
    public async Task The_cloud_metadata_endpoint_is_blocked()
    {
        var guard = Restricted(MapTo(IPAddress.Parse("169.254.169.254")));
        await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("169.254.169.254", null, default));
    }

    [Fact]
    public async Task A_hostname_that_resolves_to_a_private_IP_is_blocked()
    {
        // No real DNS involved — a stub resolver stands in for a hostile/
        // misconfigured DNS answer, exactly as the spec instructs.
        var guard = Restricted(MapTo(IPAddress.Parse("10.1.2.3")));
        var ex = await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("internal.example.test", null, default));
        Assert.Equal("internal.example.test", ex.Host);
    }

    [Fact]
    public async Task Whatever_a_decimal_encoded_IP_resolves_to_is_still_checked_post_resolution()
    {
        // Uri/DNS normalization of "2130706433" (== 127.0.0.1) is a parsing
        // concern upstream of this guard; what the guard must prove is that
        // it never trusts the host STRING — only the IPAddress it actually
        // gets back. Simulated here by resolving the literal numeral string
        // straight to the address it decodes to.
        var guard = Restricted(MapTo(IPAddress.Parse("127.0.0.1")));
        await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("2130706433", null, default));
    }

    [Fact]
    public async Task Rebinding_a_public_answer_then_a_private_one_is_blocked_on_the_second_call()
    {
        // DNS rebinding: the same host resolves public on one lookup and
        // private on the next. Proven by re-entering the guard twice — each
        // call re-resolves and re-checks; nothing is cached from the first,
        // public-looking answer.
        var calls = 0;
        Task<IPAddress[]> resolve(string host, CancellationToken ct)
        {
            calls++;
            return Task.FromResult(new[] { calls == 1 ? IPAddress.Parse("93.184.216.34") : IPAddress.Parse("127.0.0.1") });
        }
        var guard = Restricted(resolve);

        var first = await guard.ResolveAllowedAddressesAsync("rebinding.example.test", null, default);
        Assert.Equal(IPAddress.Parse("93.184.216.34"), Assert.Single(first));

        await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("rebinding.example.test", null, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_genuinely_public_address_is_allowed()
    {
        var guard = Restricted(MapTo(IPAddress.Parse("93.184.216.34")));
        var resolved = await guard.ResolveAllowedAddressesAsync("public.example.test", null, default);
        Assert.Equal(IPAddress.Parse("93.184.216.34"), Assert.Single(resolved));
    }

    [Fact]
    public async Task A_dual_stack_host_keeps_its_public_candidate_even_when_another_candidate_is_denied()
    {
        // Real regression, caught live verifying D6-T7's quickstart doc: "localhost" resolves to
        // both ::1 (denied) and 127.0.0.1 (also denied, but the point here is dual-stack mixed
        // reachability in general) — a single-address version of this method picked whichever
        // came first and gave the caller no way to fall back. The fix: filter out only the denied
        // candidates, keep the rest, in resolution order — never collapse to one address before
        // the caller even gets a chance to try connecting.
        Task<IPAddress[]> resolve(string host, CancellationToken ct) =>
            Task.FromResult(new[] { IPAddress.Parse("169.254.169.254"), IPAddress.Parse("93.184.216.34") });
        var guard = Restricted(resolve);

        var allowed = await guard.ResolveAllowedAddressesAsync("mixed.example.test", null, default);

        Assert.Equal(IPAddress.Parse("93.184.216.34"), Assert.Single(allowed));
    }

    [Fact]
    public async Task An_allow_listed_target_is_permitted_for_its_own_tenant_and_still_blocked_for_another()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Outbound:Mode"] = "Restricted",
            [$"Outbound:AllowList:{tenantA}:0"] = "10.9.9.9",
        }).Build();
        var guard = new ConfiguredOutboundGuard(config, MapTo(IPAddress.Parse("10.9.9.9")));

        var allowed = await guard.ResolveAllowedAddressesAsync("internal.example.test", tenantA, default);
        Assert.Equal(IPAddress.Parse("10.9.9.9"), Assert.Single(allowed));

        await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("internal.example.test", tenantB, default));
        await Assert.ThrowsAsync<BlockedTargetException>(() => guard.ResolveAllowedAddressesAsync("internal.example.test", null, default));
    }

    [Fact]
    public async Task IPv6_loopback_and_link_local_are_blocked()
    {
        var loopback = Restricted(MapTo(IPAddress.Parse("::1")));
        await Assert.ThrowsAsync<BlockedTargetException>(() => loopback.ResolveAllowedAddressesAsync("::1", null, default));

        var linkLocal = Restricted(MapTo(IPAddress.Parse("fe80::1")));
        await Assert.ThrowsAsync<BlockedTargetException>(() => linkLocal.ResolveAllowedAddressesAsync("fe80::1", null, default));
    }

    /// <summary>
    /// Mutation check, recorded directly in the test (Day 6 spec's own
    /// required evidence): the exact same target that Restricted mode
    /// blocks above is let through when the protection is switched off.
    /// This is what proves the Restricted-mode tests above are actually
    /// exercising the guard, not passing for an unrelated reason.
    /// </summary>
    [Fact]
    public async Task Mutation_check_the_same_private_target_passes_once_Restricted_mode_is_turned_off()
    {
        var permissive = new ConfiguredOutboundGuard(Config("Permissive"), MapTo(IPAddress.Parse("10.0.0.1")));
        var resolved = await permissive.ResolveAllowedAddressesAsync("10.0.0.1", null, default);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), Assert.Single(resolved));
    }

    // --- Plumbing: the guard actually wired into ApiModule's real HTTP pipeline ---

    [Fact]
    public async Task ApiModule_with_a_restricted_guard_fails_the_step_instead_of_connecting_to_a_private_target()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var guard = new ConfiguredOutboundGuard(Config("Restricted"));
        var module = new ApiModule(guard);
        var outcome = await module.Run("request",
            new Dictionary<string, object?> { ["url"] = $"http://127.0.0.1:{port}/" },
            FakeContext(), default);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("Blocked", outcome.Error);
    }

    [Fact]
    public async Task ApiModule_with_no_guard_supplied_still_reaches_a_local_target_exactly_as_before_D6_T5()
    {
        using var server = new SelfClosingLocalServer();
        var module = new ApiModule(); // no guard — every pre-existing construction site
        var outcome = await module.Run("request",
            new Dictionary<string, object?> { ["url"] = server.Url, ["expectedStatus"] = 200 },
            FakeContext(), default);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    private sealed class FixedAddressesGuard : IOutboundGuard
    {
        private readonly IPAddress[] _addresses;
        public FixedAddressesGuard(params IPAddress[] addresses) => _addresses = addresses;
        public Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(string host, Guid? tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(_addresses);
    }

    [Fact]
    public async Task ApiModule_falls_through_to_the_next_allowed_address_when_the_first_refuses_the_connection()
    {
        // The actual regression this test locks in: a guard that returns multiple allowed
        // addresses for one host (a dual-stack "localhost", in the real bug) must let ApiModule
        // try the next one when the first can't actually be connected to — not surface a bare
        // connection-refused and give up, which is what a single-address return used to do.
        //
        // Uses ::1 vs 127.0.0.1 specifically — not a secondary loopback alias like 127.0.0.2 —
        // because the first version of this test used 127.0.0.2 and failed on this exact PR's own
        // Linux CI run: that address range isn't guaranteed to behave the same way (fast,
        // deterministic "connection refused") across every OS/sandbox. ::1 and 127.0.0.1 are both
        // always-configured, standard loopback interfaces everywhere, and this pairing is the
        // literal real bug (IPv6 loopback resolving before IPv4), not a stand-in for it.
        using var server = new SelfClosingLocalServer();

        // FixedAddressesGuard ignores the host it's given for connecting — ApiModule's
        // ConnectCallback tries the guard's two fixed addresses, never DNS. But the URL's host
        // still becomes the literal HTTP Host header sent to the server, and Linux's managed
        // HttpListener (unlike Windows' http.sys) refuses a request whose Host header doesn't
        // match the registered prefix's authority — this test failed on that mismatch on Linux
        // CI even after the real ConnectCallback bug below was fixed. Using server.Url's own
        // host (127.0.0.1, the same literal every other HttpListener-backed test here already
        // uses) keeps the Host header valid everywhere while the guard still decides, underneath,
        // which address the socket actually connects to.
        var guard = new FixedAddressesGuard(IPAddress.IPv6Loopback, IPAddress.Loopback); // ::1 refuses (nothing listens there); 127.0.0.1 is the real server
        var module = new ApiModule(guard);
        var outcome = await module.Run("request",
            new Dictionary<string, object?> { ["url"] = server.Url, ["expectedStatus"] = 200 },
            FakeContext(), default);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    private static ExecutionContext FakeContext() => new()
    {
        RunId = new Yukti.Domain.SharedKernel.FlowRunId(Guid.NewGuid()),
        Variables = new Dictionary<string, object?>(),
        Credentials = new NoCredentials(),
        RunCancellation = default,
    };

    private sealed class NoCredentials : Yukti.Contracts.ICredentialResolver
    {
        public Task<string?> ResolveAsync(string credentialReference, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    /// <summary>Minimal real HTTP server on an ephemeral loopback port — just
    /// enough to prove ApiModule's default (unguarded) path still completes a
    /// real request end-to-end, the regression guard for every pre-existing
    /// flow that targets FixtureApp/localhost.</summary>
    private sealed class SelfClosingLocalServer : IDisposable
    {
        private readonly System.Net.HttpListener _listener;
        public string Url { get; }

        public SelfClosingLocalServer()
        {
            var port = GetFreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener = new System.Net.HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
                catch (System.Net.HttpListenerException) { /* stopped under us — fine */ }
                catch (ObjectDisposedException) { /* stopped under us — fine */ }
            });
        }

        private static int GetFreePort()
        {
            using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}
