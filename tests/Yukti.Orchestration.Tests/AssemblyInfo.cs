using Xunit;

// FlowRunTelemetryTests listens on the process-wide, static
// OrchestrationTelemetry.ActivitySource/Meter — xunit's default
// cross-class parallelism would let another test class's FlowEngine.Execute
// call emit activities/measurements into a listener that's mid-assertion
// in this class, and vice versa. Serializing this assembly's tests avoids
// that cross-talk; the suite is small enough that this costs nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
