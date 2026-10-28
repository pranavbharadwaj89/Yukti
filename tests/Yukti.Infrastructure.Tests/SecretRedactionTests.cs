using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.FlowAuthoring;
using Yukti.Cli;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T3a, re-scoped: v0.1 has no <c>{{secrets}}</c> syntax (that pipeline is v0.2 — see
/// DISCOVERED), so the real, current risk is a user pasting a live credential straight into an
/// API step's header, most commonly <c>Authorization</c>. These tests prove the sentinel never
/// reaches any surface a flow author didn't put it in on purpose: the JSON report, JUnit XML,
/// step result data, application logs, or the audit trail. The flow DEFINITION itself (what the
/// API's own flow-edit endpoints return) is explicitly NOT in scope — the author typed it there,
/// they see it back; <c>docs/known-limitations.md</c> states this plainly.
/// </summary>
public sealed class SecretRedactionTests : IAsyncLifetime
{
    private const string Sentinel = "SENTINEL-d41d8cd98f00";

    private HttpListener _listener = null!;
    private string _baseUrl = null!;
    private Task _serverTask = null!;

    public Task InitializeAsync()
    {
        var port = GetFreePort();
        _baseUrl = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _serverTask = Task.Run(async () =>
        {
            try
            {
                while (_listener.IsListening)
                {
                    var ctx = await _listener.GetContextAsync();
                    // Deliberately never reads/echoes the Authorization header — this stub
                    // proves OUR code doesn't leak the sentinel, not whether some server does.
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    var body = System.Text.Encoding.UTF8.GetBytes("""{"status":"ok"}""");
                    ctx.Response.OutputStream.Write(body);
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        return Task.CompletedTask;
    }

    private static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static FlowEngine BuildEngine()
    {
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Debug));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);
        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));
    }

    private Dictionary<string, object?> StepParamsWithAuthHeader(int? expectedStatus = null)
    {
        var @params = new Dictionary<string, object?>
        {
            ["url"] = _baseUrl,
            ["method"] = "GET",
            ["headers"] = new Dictionary<string, object?> { ["Authorization"] = $"Bearer {Sentinel}" },
        };
        if (expectedStatus is { } status)
            @params["expectedStatus"] = status;
        return @params;
    }

    private async Task<(Flow flow, FlowRun run)> RunOneApiStep(int? expectedStatus = null)
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("D6-T3a header redaction flow", null, tenantId, UserId.New());
        flow.AddStep("Call with Authorization header", ModuleKind.Api, "request", StepParamsWithAuthHeader(expectedStatus));
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        run = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
        return (flow, run);
    }

    [Fact]
    public async Task A_passing_step_never_leaks_the_header_sentinel_in_the_JSON_report_or_step_data()
    {
        var (_, run) = await RunOneApiStep();
        Assert.Equal(RunStatus.Passed, run.Status);

        var json = JsonReportWriter.Write(run);
        Assert.DoesNotContain(Sentinel, json);

        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(run.Results.Single().Data));
    }

    [Fact]
    public async Task A_failing_step_never_leaks_the_header_sentinel_in_the_JUnit_report()
    {
        // expectedStatus mismatch — the request itself still carries the real header; only the
        // assertion fails, so the JUnit writer's failure path (params + data excerpt) is the one
        // actually exercised here, not the plain-pass path above.
        var (flow, run) = await RunOneApiStep(expectedStatus: 999);
        Assert.Equal(RunStatus.Failed, run.Status);

        var junit = JUnitReportWriter.Write(flow, run).ToString();
        Assert.DoesNotContain(Sentinel, junit);
        Assert.Contains("[REDACTED]", junit);
    }

    [Fact]
    public async Task Application_logs_never_contain_the_header_sentinel()
    {
        var captured = new System.Text.StringBuilder();
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingLoggerProvider(captured)));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);
        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("D6-T3a logging flow", null, tenantId, UserId.New());
        flow.AddStep("Call with Authorization header", ModuleKind.Api, "request", StepParamsWithAuthHeader());
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.DoesNotContain(Sentinel, captured.ToString());
    }

    [Fact]
    public async Task Application_logs_never_contain_the_secrets_auth_sentinel()
    {
        var captured = new System.Text.StringBuilder();
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingLoggerProvider(captured)));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);
        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("API-T4 secrets-auth logging flow", null, tenantId, UserId.New());
        flow.AddStep("Call with secrets-resolved bearer auth", ModuleKind.Api, "request", new Dictionary<string, object?>
        {
            ["url"] = _baseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "{{secrets.apiToken}}" },
        });
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var credentials = new InMemoryCredentialResolver(new Dictionary<string, string> { ["apiToken"] = Sentinel });
        await engine.Execute(flow, run, credentials, CancellationToken.None);

        Assert.DoesNotContain(Sentinel, captured.ToString());
    }

    [Fact]
    public async Task Audit_entries_never_contain_the_header_sentinel()
    {
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var flows = new InMemoryFlowRepository(uowFactory);
        var audit = new InMemoryAuditRepository();
        var tenantId = TenantId.New();
        var tenantAccessor = new FixedTenantContextAccessor(tenantId);
        var flow = Flow.CreateDraft("D6-T3a audit flow", null, tenantId, UserId.New());
        await flows.Save(flow, CancellationToken.None);
        await using (var uow = uowFactory.Create()) await uow.Commit(CancellationToken.None);

        var handler = new AddFlowStepCommandHandler(flows, new AllowAllPermissionChecker(), new TenantGuard(tenantAccessor), uowFactory, audit, tenantAccessor);
        await handler.Handle(new AddFlowStepCommand(
            flow.Id, "Call with Authorization header", ModuleKind.Api, "request",
            StepParamsWithAuthHeader(), null, null, UserId.New()), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        var metadataJson = JsonSerializer.Serialize(entry.Metadata);
        Assert.DoesNotContain(Sentinel, metadataJson);
        Assert.Contains("[REDACTED]", metadataJson);
    }

    // ---- API-T4: {{secrets.name}}-resolved auth must leak nowhere either ----
    // Distinct from the header-literal tests above (D6-T3a's own scope, "no {{secrets}} syntax
    // yet") -- these exercise the real pipeline: VariableStore resolves {{secrets.apiToken}}
    // through ICredentialResolver, ApiModule's `auth` builds the real Authorization header from
    // it, and the resolved value must still never reach any surface. _receivedAuthHeader proves
    // the real sentinel value was genuinely sent (ruling out the vacuous case where the test
    // would also pass if auth silently did nothing) while the assertions below prove it reaches
    // none of the surfaces a flow author didn't put it in on purpose.

    private string? _receivedAuthHeader;

    private async Task<(Flow flow, FlowRun run)> RunOneApiStepWithSecretsAuth(int? expectedStatus = null)
    {
        _receivedAuthHeader = null;
        var captureListener = new HttpListener();
        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}/";
        captureListener.Prefixes.Add(baseUrl);
        captureListener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (captureListener.IsListening)
                {
                    var ctx = await captureListener.GetContextAsync();
                    _receivedAuthHeader = ctx.Request.Headers["Authorization"];
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    var body = System.Text.Encoding.UTF8.GetBytes("""{"status":"ok"}""");
                    ctx.Response.OutputStream.Write(body);
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });

        try
        {
            var engine = BuildEngine();
            var tenantId = TenantId.New();
            var flow = Flow.CreateDraft("API-T4 secrets-auth redaction flow", null, tenantId, UserId.New());
            var @params = new Dictionary<string, object?>
            {
                ["url"] = baseUrl,
                ["method"] = "GET",
                ["auth"] = new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "{{secrets.apiToken}}" },
            };
            if (expectedStatus is { } status)
                @params["expectedStatus"] = status;
            flow.AddStep("Call with secrets-resolved bearer auth", ModuleKind.Api, "request", @params);

            var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
            var credentials = new InMemoryCredentialResolver(new Dictionary<string, string> { ["apiToken"] = Sentinel });
            run = await engine.Execute(flow, run, credentials, CancellationToken.None);
            return (flow, run);
        }
        finally
        {
            captureListener.Stop();
            captureListener.Close();
        }
    }

    [Fact]
    public async Task A_passing_step_using_secrets_auth_never_leaks_the_sentinel_in_the_JSON_report_or_step_data()
    {
        var (_, run) = await RunOneApiStepWithSecretsAuth();
        Assert.Equal(RunStatus.Passed, run.Status);

        // Proves the real sentinel was genuinely resolved and sent -- not a vacuous pass because
        // auth silently did nothing.
        Assert.Equal($"Bearer {Sentinel}", _receivedAuthHeader);

        var json = JsonReportWriter.Write(run);
        Assert.DoesNotContain(Sentinel, json);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(run.Results.Single().Data));
    }

    [Fact]
    public async Task A_failing_step_using_secrets_auth_never_leaks_the_sentinel_in_the_JUnit_report()
    {
        var (flow, run) = await RunOneApiStepWithSecretsAuth(expectedStatus: 999);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal($"Bearer {Sentinel}", _receivedAuthHeader);

        var junit = JUnitReportWriter.Write(flow, run).ToString();
        Assert.DoesNotContain(Sentinel, junit);
        Assert.Contains("[REDACTED]", junit);
    }

    [Fact]
    public async Task The_flows_own_authored_params_contain_only_the_placeholder_never_the_resolved_secret()
    {
        // The strongest proof that the resolved value can't leak: it was never written anywhere
        // persistent to begin with. FlowStep.Params is exactly what the author typed --
        // Interpolate's result lives only in FlowEngine's local `interpolatedParams`, handed to
        // the module and discarded.
        var (flow, _) = await RunOneApiStepWithSecretsAuth();
        var authoredParams = JsonSerializer.Serialize(flow.Steps.Single().Params);

        Assert.Contains("{{secrets.apiToken}}", authoredParams);
        Assert.DoesNotContain(Sentinel, authoredParams);
    }

    [Fact]
    public async Task A_missing_secret_fails_the_step_naming_the_secret_never_falling_back_to_sending_nothing()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Missing secret flow", null, tenantId, UserId.New());
        flow.AddStep("Call with an unresolvable secret", ModuleKind.Api, "request", new Dictionary<string, object?>
        {
            ["url"] = _baseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "{{secrets.doesNotExist}}" },
        });

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        run = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(RunStatus.Failed, run.Status);
        var result = run.Results.Single();
        Assert.Contains("doesNotExist", result.Error);
        Assert.Null(result.Data); // never dispatched -- no request was ever sent
    }

    private sealed class FixedTenantContextAccessor : ITenantContextAccessor
    {
        public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
        public TenantId? CurrentTenantId { get; }
    }

    private sealed class AllowAllPermissionChecker : IPermissionChecker
    {
        public Task EnsurePermission(UserId userId, Permission required, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly System.Text.StringBuilder _sink;
        public CapturingLoggerProvider(System.Text.StringBuilder sink) => _sink = sink;
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_sink);
        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly System.Text.StringBuilder _sink;
            public CapturingLogger(System.Text.StringBuilder sink) => _sink = sink;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_sink) _sink.AppendLine(formatter(state, exception));
            }
        }
    }
}
