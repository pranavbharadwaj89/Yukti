namespace Yukti.Cli.Import;

/// <summary>
/// API Testing Module Spec, API-T10: the shared output shape for both importers. `Flow` is the
/// same `FlowFileDto` `FlowLoader`/`run` already consume unchanged — an importer's whole job is
/// producing that external contract, nothing more; it never touches `CreateFlowCommand`,
/// `AddFlowStepCommand`, or `Flow.Publish` itself. `Warnings` is never empty-by-omission: anything
/// the source document asked for that has no Yukti equivalent (a pre-request script, a body mode
/// Yukti doesn't model, an auth scheme beyond basic/bearer) is named here — "a partial import that
/// looks complete is worse than a refused one," the spec's own words.
/// </summary>
public sealed record ImportResult(FlowFileDto? Flow, IReadOnlyList<string> Warnings)
{
    public static ImportResult Failed(string reason) => new(null, new List<string> { reason });
}
