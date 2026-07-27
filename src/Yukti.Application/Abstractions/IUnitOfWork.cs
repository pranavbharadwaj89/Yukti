namespace Yukti.Application.Abstractions;

/// <summary>
/// Commits a staged repository change and dispatches any domain events the
/// aggregate raised, atomically. Deliberately minimal — no exposed
/// BeginTransaction/Rollback; transactional scope is managed internally.
/// (Volume 1 Part III §16.2)
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task Commit(CancellationToken ct);

    // FR-OPS-03 fallout: AuditableCommandHandler now stages a command's own
    // outcome and commits it in the SAME round trip as its business state
    // (merging what used to be two separate SaveChangesAsync calls) — but
    // that means if HandleCore staged a partial mutation before throwing
    // (e.g. loaded-and-mutated-in-place aggregate via EF's own change
    // tracking), that partial state must not be swept up into the
    // failure-path commit alongside the failure's own audit entry. This is
    // the one exception to "no exposed Rollback" above: discarding
    // everything staged so far, never committing it.
    void DiscardStaged();
}

/// <summary>
/// Constructs independent, short-lived IUnitOfWork instances on demand.
/// Exists specifically to serve the Flow Engine's per-step incremental
/// commit pattern (§16.7) — a single FlowRun execution spans many
/// independent commit boundaries (one per step), not the one-commit-per-
/// command pattern that correctly serves ordinary authoring commands.
/// (Volume 1 Part III §16.7)
/// </summary>
public interface IUnitOfWorkFactory
{
    IUnitOfWork Create();
}
