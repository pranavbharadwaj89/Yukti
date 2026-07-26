using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Yukti.Orchestration;

/// <summary>
/// FR-OBS-01/02 (Volume 1 Part IV §29.3-29.4): the single ActivitySource
/// (traces) and Meter (metrics) every orchestration component emits
/// through. Kept as one static surface, not one per class, so an
/// OpenTelemetry SDK host only ever needs to AddSource/AddMeter this one
/// name ("Yukti.Orchestration") to capture everything FlowEngine,
/// RetryFlakeHandler, and ModuleDispatcher produce — no per-component
/// wiring, and no dependency on the OTel SDK itself from this project
/// (System.Diagnostics.DiagnosticSource's Activity/Meter types are
/// part of the BCL; a listener is what turns them into real traces/metrics).
///
/// ai.request.duration / ai.request.timeout are declared here to satisfy
/// FR-OBS-02's named-metric inventory, but stay at zero until an AI module
/// (FR-PLUGIN's Ai ModuleKind) actually exists — there is no AI module
/// implementation in this codebase yet to emit them under real load.
/// </summary>
public static class OrchestrationTelemetry
{
    public const string SourceName = "Yukti.Orchestration";

    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    private static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Histogram<double> StepDispatchDuration = Meter.CreateHistogram<double>(
        "step.dispatch.duration", unit: "ms",
        description: "Time from a step's retry-wrapped dispatch starting to its final outcome, per attempt cycle.");

    public static readonly Counter<long> FlowRunFlakeDetected = Meter.CreateCounter<long>(
        "flow.run.flake_detected",
        description: "Incremented once per step that failed at least once but ultimately passed within its retry budget.");

    public static readonly Counter<long> FlowRunCompleted = Meter.CreateCounter<long>(
        "flow.run.completed",
        description: "Incremented once per FlowRun reaching a terminal status, tagged by that status.");

    public static readonly UpDownCounter<long> OrchestrationConcurrentExecutions = Meter.CreateUpDownCounter<long>(
        "orchestration.concurrent_executions",
        description: "Number of FlowRun.Execute calls currently in flight across this process.");

    public static readonly Histogram<double> AiRequestDuration = Meter.CreateHistogram<double>(
        "ai.request.duration", unit: "ms",
        description: "Time from an AI module request starting to its response. Unpopulated until an AI module exists.");

    public static readonly Counter<long> AiRequestTimeout = Meter.CreateCounter<long>(
        "ai.request.timeout",
        description: "Incremented once per AI module request that timed out. Unpopulated until an AI module exists.");
}
