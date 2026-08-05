using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Integration evidence for FR-OBS-01 (one root trace + one child span per
/// step) and FR-OBS-02 (the named metric inventory, populated under a real
/// run) — attaches a real ActivityListener/MeterListener, the same
/// mechanism an OpenTelemetry SDK host uses, and inspects what actually
/// got emitted.
/// </summary>
public sealed class FlowRunTelemetryTests
{
    private static (FlowEngine Engine, ICredentialResolver Credentials) BuildEngine(IAutomationModule module)
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(module);

        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>());

        return (engine, new InMemoryCredentialResolver());
    }

    private static (Flow Flow, FlowRun Run) BuildThreeStepFlow(ModuleKind module, string action)
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Telemetry test flow", null, tenantId, UserId.New());
        for (var i = 0; i < 3; i++)
            flow.AddStep($"Step {i + 1}", module, action, new Dictionary<string, object?>());
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return (flow, run);
    }

    [Fact]
    public async Task Three_step_run_produces_one_root_span_and_three_child_spans()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OrchestrationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var (engine, credentials) = BuildEngine(new SecretTouchingModule());
        var (flow, run) = BuildThreeStepFlow(SecretTouchingModule.Kind, "call");

        await engine.Execute(flow, run, credentials, CancellationToken.None);

        var roots = captured.Where(a => a.OperationName == "FlowRun.Execute").ToList();
        var children = captured.Where(a => a.OperationName == "FlowStep.Execute").ToList();

        Assert.Single(roots);
        Assert.Equal(3, children.Count);
        Assert.All(children, child => Assert.Equal(roots[0].SpanId, child.ParentSpanId));
    }

    [Fact]
    public async Task Run_populates_step_dispatch_duration_and_flow_run_completed_metrics()
    {
        var measurements = new List<(string Instrument, object? Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OrchestrationTelemetry.SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.Start();

        var (engine, credentials) = BuildEngine(new SecretTouchingModule());
        var (flow, run) = BuildThreeStepFlow(SecretTouchingModule.Kind, "call");

        await engine.Execute(flow, run, credentials, CancellationToken.None);
        listener.Dispose();

        Assert.Equal(3, measurements.Count(m => m.Instrument == "step.dispatch.duration"));
        Assert.Single(measurements, m => m.Instrument == "flow.run.completed");
    }

    [Fact]
    public void Meter_declares_exactly_the_six_named_metrics_from_FR_OBS_02()
    {
        var expected = new[]
        {
            "step.dispatch.duration",
            "flow.run.flake_detected",
            "flow.run.completed",
            "orchestration.concurrent_executions",
            "ai.request.duration",
            "ai.request.timeout"
        };

        // Touching each static field forces the Meter to have created its
        // instruments before we enumerate — static readonly fields
        // otherwise only initialize on first access to the class.
        _ = OrchestrationTelemetry.StepDispatchDuration;
        _ = OrchestrationTelemetry.FlowRunFlakeDetected;
        _ = OrchestrationTelemetry.FlowRunCompleted;
        _ = OrchestrationTelemetry.OrchestrationConcurrentExecutions;
        _ = OrchestrationTelemetry.AiRequestDuration;
        _ = OrchestrationTelemetry.AiRequestTimeout;

        var names = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == OrchestrationTelemetry.SourceName)
                names.Add(instrument.Name);
        };
        listener.Start();
        listener.Dispose();

        Assert.Equal(expected.OrderBy(n => n), names.Distinct().OrderBy(n => n));
    }
}
