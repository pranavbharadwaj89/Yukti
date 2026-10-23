using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// D6-T5: shared by WebModule and DesktopUiModule's <c>screenshot</c> actions, and by
/// Yukti.Api's screenshot-serving endpoint (Program.cs) — one directory per FlowRunId so the
/// serving endpoint's only authorization check can be <see cref="Yukti.Application.Abstractions.IFlowRunRepository"/>.GetById,
/// reusing FR-TENANT-01's existing repository/RLS tenant filter instead of a new mechanism: a run
/// a caller's tenant can't load, it can't see the screenshot directory for either.
/// </summary>
public static class ScreenshotStorage
{
    public static string RootDirectory =>
        Environment.GetEnvironmentVariable("YUKTI_SCREENSHOTS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

    /// <summary>A flow author controls the raw value via the step's "path" param — this must never
    /// be able to escape the per-run directory (e.g. "../../secrets.txt"), so only the bare file
    /// name survives.</summary>
    public static string SafeFileName(string? requested, string defaultPrefix) =>
        requested is null ? $"{defaultPrefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.png" : Path.GetFileName(requested);

    public static string DirectoryFor(FlowRunId runId) => Path.Combine(RootDirectory, runId.Value.ToString());

    public static string FullPath(FlowRunId runId, string fileName) => Path.Combine(DirectoryFor(runId), fileName);

    /// <summary>Called by the modules before writing — the serving endpoint only ever reads, so it
    /// uses <see cref="FullPath"/> directly instead.</summary>
    public static string PrepareFullPath(FlowRunId runId, string fileName)
    {
        var directory = DirectoryFor(runId);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
