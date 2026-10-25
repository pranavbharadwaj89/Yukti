using System.Net;
using FixtureApp;
using Microsoft.AspNetCore.Builder;
using Xunit;
using Yukti.Application.Security;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// D6-T5's "the same set runs against the Web module too" requirement —
/// proven directly against WebModule's own Setup/Run/Teardown, through the
/// real Chromium process and the real local OutboundGuardProxy, not a
/// theoretical description of the wiring. The deny-list's own CIDR math is
/// already proven exhaustively in Yukti.Infrastructure.Tests.OutboundGuardTests
/// against ConfiguredOutboundGuard directly; these tests exist to prove the
/// plumbing from WebModule through the proxy into the guard is real.
/// </summary>
public sealed class WebModuleOutboundGuardTests : IAsyncLifetime
{
    private WebApplication _fixtureApp = null!;
    private string _baseUrl = null!;

    public async Task InitializeAsync()
    {
        _fixtureApp = FixtureAppHost.Build(logPath: Path.Combine(Path.GetTempPath(), $"webmodule-guard-test-{Guid.NewGuid():N}.log"));
        await _fixtureApp.StartAsync();
        _baseUrl = _fixtureApp.Urls.First();
    }

    public async Task DisposeAsync() => await _fixtureApp.DisposeAsync();

    private sealed class BlockingGuard : IOutboundGuard
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(string host, Guid? tenantId, CancellationToken ct) =>
            throw new BlockedTargetException(host, IPAddress.Parse("169.254.169.254"));
    }

    private static ExecutionContext FakeContext() => new()
    {
        RunId = new FlowRunId(Guid.NewGuid()),
        Variables = new Dictionary<string, object?>(),
        Credentials = new NoCredentials(),
        RunCancellation = default,
    };

    private sealed class NoCredentials : Yukti.Contracts.ICredentialResolver
    {
        public Task<string?> ResolveAsync(string credentialReference, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task A_restricted_guard_blocks_navigation_to_the_metadata_endpoint()
    {
        var module = new WebModule(new BlockingGuard());
        var ctx = FakeContext();
        await module.Setup(ctx, default);
        try
        {
            var outcome = await module.Run("navigate",
                new Dictionary<string, object?> { ["url"] = "http://169.254.169.254/latest/meta-data/" },
                ctx, default);

            Assert.Equal(StepStatus.Failed, outcome.Status);
        }
        finally
        {
            await module.Teardown(ctx, default);
        }
    }

    [Fact]
    public async Task With_no_guard_supplied_WebModule_still_reaches_a_real_local_target_exactly_as_before_D6_T5()
    {
        var module = new WebModule(); // default — every pre-existing construction site
        var ctx = FakeContext();
        await module.Setup(ctx, default);
        try
        {
            var outcome = await module.Run("navigate",
                new Dictionary<string, object?> { ["url"] = _baseUrl },
                ctx, default);

            Assert.Equal(StepStatus.Passed, outcome.Status);
        }
        finally
        {
            await module.Teardown(ctx, default);
        }
    }
}
