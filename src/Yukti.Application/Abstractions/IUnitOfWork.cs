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
