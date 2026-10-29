using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Web UI Testing Module Spec, WEB-T8: "a flow can save its logged-in state and later flows can
/// start from it, skipping login... storage state is tenant-scoped." File-based, not a new EF
/// aggregate -- same reasoning CLAUDE.md's "modules get only ExecutionContext, never DbContext"
/// rule already gives, and the exact mechanism WebModule's own screenshot/trace/video artifacts
/// already use (ScreenshotStorage). A DB-backed repository would make WebModule (registered as a
/// DI Singleton) captive-depend on the Scoped YuktiDbContext -- confirmed as a real crash by
/// `dotnet ef migrations remove` itself while this was being built, not a theoretical concern.
///
/// Tenant-scoped by directory, not a DB row + RLS policy: the path is built server-side from
/// ExecutionContext.TenantId (never client-supplied), the same trust model ScreenshotStorage
/// already relies on for FlowRunId.
/// </summary>
public static class BrowserStorageStateStorage
{
    public static string RootDirectory =>
        Environment.GetEnvironmentVariable("YUKTI_STORAGE_STATES_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "storage-states");

    public static string DirectoryFor(TenantId tenantId) => Path.Combine(RootDirectory, tenantId.Value.ToString());

    /// <summary>A flow author controls the raw name via the step's "name" param -- same
    /// path-traversal risk ScreenshotStorage.SafeFileName already guards against (e.g.
    /// "../../secrets"), so only the bare file name survives.</summary>
    private static string SafeFileName(string name)
    {
        var fileName = Path.GetFileName(name);
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException($"'{name}' is not a valid storage state name.");
        return fileName + ".json";
    }

    public static string FullPath(TenantId tenantId, string name) => Path.Combine(DirectoryFor(tenantId), SafeFileName(name));

    public static string PrepareFullPath(TenantId tenantId, string name)
    {
        var directory = DirectoryFor(tenantId);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, SafeFileName(name));
    }
}
