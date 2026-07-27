using Yukti.Application.Abstractions;
using Yukti.Domain.Auditing;

namespace Yukti.Application.Auditing;

/// <summary>
/// FR-AUDIT-01: every command handler inherits this instead of
/// implementing ICommandHandler directly, unless explicitly exempted (see
/// NoUnauditedCommandHandlerAnalyzer / YUKTI002 in Yukti.Analyzers, which
/// enforces that at compile time). Handle() itself is sealed — subclasses
/// implement HandleCore with their actual business logic, and this base
/// class is the only place that ever constructs or appends an AuditEntry,
/// so there is exactly one code path where a command's audit trail can be
/// silently skipped: never.
///
/// Appends on both success AND failure — a rejected/failed command attempt
/// is exactly the kind of event an audit trail exists to capture, not
/// something to omit because it didn't "complete."
///
/// FR-OPS-03: HandleCore no longer commits its own business state — it
/// only stages it (repository .Save() calls, which just track/queue), and
/// this class commits it together with the audit entry in exactly one
/// IUnitOfWork.Commit() call, saving a whole DB round trip per command
/// versus the previous "business commit, then separate audit commit"
/// shape. On the failure path, DiscardStaged() runs first — discarding
/// whatever HandleCore staged before throwing — so only the failure's own
/// audit entry ends up in that commit, never a half-done mutation.
/// </summary>
public abstract class AuditableCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IAuditRepository _audit;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IUnitOfWorkFactory _uowFactory;

    protected AuditableCommandHandler(IAuditRepository audit, ITenantContextAccessor tenantAccessor, IUnitOfWorkFactory uowFactory)
    {
        _audit = audit;
        _tenantAccessor = tenantAccessor;
        _uowFactory = uowFactory;
    }

    public async Task<TResult> Handle(TCommand command, CancellationToken ct)
    {
        var metadata = AuditMetadataBuilder.Capture(command);
        var tenantId = _tenantAccessor.CurrentTenantId;
        try
        {
            var result = await HandleCore(command, ct);
            await _audit.Append(AuditEntry.Capture(typeof(TCommand).Name, tenantId, succeeded: true, failureReason: null, metadata), ct);
            await using var uow = _uowFactory.Create();
            await uow.Commit(ct);
            return result;
        }
        catch (Exception ex)
        {
            await using var uow = _uowFactory.Create();
            uow.DiscardStaged(); // order matters: before staging the failure entry, not after
            await _audit.Append(AuditEntry.Capture(typeof(TCommand).Name, tenantId, succeeded: false, ex.Message, metadata), ct);
            await uow.Commit(ct);
            throw;
        }
    }

    protected abstract Task<TResult> HandleCore(TCommand command, CancellationToken ct);
}
