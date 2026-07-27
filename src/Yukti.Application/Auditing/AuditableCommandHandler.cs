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
/// </summary>
public abstract class AuditableCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IAuditRepository _audit;

    protected AuditableCommandHandler(IAuditRepository audit) => _audit = audit;

    public async Task<TResult> Handle(TCommand command, CancellationToken ct)
    {
        var metadata = AuditMetadataBuilder.Capture(command);
        try
        {
            var result = await HandleCore(command, ct);
            await _audit.Append(AuditEntry.Capture(typeof(TCommand).Name, succeeded: true, failureReason: null, metadata), ct);
            return result;
        }
        catch (Exception ex)
        {
            await _audit.Append(AuditEntry.Capture(typeof(TCommand).Name, succeeded: false, ex.Message, metadata), ct);
            throw;
        }
    }

    protected abstract Task<TResult> HandleCore(TCommand command, CancellationToken ct);
}
