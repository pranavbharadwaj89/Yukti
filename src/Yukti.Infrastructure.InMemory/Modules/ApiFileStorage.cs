namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// API-T3: the area ApiModule's multipart file parts read from — a single configured directory,
/// never an arbitrary filesystem path a flow author's "fileName" value could otherwise name. A
/// flow author controls the raw value via the step's own param, so only the bare file name
/// survives (mirrors ScreenshotStorage.SafeFileName's own reasoning, the existing convention for
/// a flow-author-controlled path in this codebase — "../../secrets.txt" must never escape this
/// directory).
///
/// Deliberately not per-run or per-tenant like ScreenshotStorage's directories: those are
/// written by the system itself after a run starts, so a RunId already exists to scope them by.
/// A fixture an API test uploads must exist here *before* the run that references it even
/// starts, and there is no run-scoped (or tenant-scoped) upload mechanism yet to stage one by —
/// building one is out of this task's scope. Until one exists, this is a single, operator-
/// managed area shared across tenants; treat it as read-only test fixtures, not user data.
/// </summary>
public static class ApiFileStorage
{
    public static string RootDirectory =>
        Environment.GetEnvironmentVariable("YUKTI_API_FILES_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "api-files");

    /// <summary>Null for an empty/whitespace-only name (Path.GetFileName can return "" for
    /// input like "" or a path ending in a separator) — the caller treats that the same as
    /// "file not found" rather than silently resolving to the root directory itself.</summary>
    public static string? ResolveSafePath(string requestedFileName)
    {
        var fileName = Path.GetFileName(requestedFileName);
        return string.IsNullOrEmpty(fileName) ? null : Path.Combine(RootDirectory, fileName);
    }
}
