using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Ai;

public sealed record GenerateFlowCommand(string Description, TenantId TenantId, UserId RequestedBy) : ICommand<GenerateFlowResult>;

/// <summary>
/// D5-T7: the orchestration English → Analyst → Coder always needed but never had a caller for
/// (D5-T4/T5 built the two capabilities; nothing chained them until now). A discriminated result
/// rather than throwing for the ambiguous case — Analyst's own refusal to guess (D5-T4) is an
/// expected, reportable outcome for the UI's "clarifying question" state, not exceptional program
/// state (Architecture Principle 20.4, the same reasoning <see cref="Flow.Publish"/> already uses).
/// </summary>
public abstract record GenerateFlowResult
{
    /// <summary>The Draft Flow is already saved — <see cref="FlowId"/>, not the aggregate itself,
    /// since the caller (the API endpoint) only needs enough to redirect the UI to it.</summary>
    public sealed record Generated(FlowId FlowId, string Capability, double Confidence, string ModelIdentifier) : GenerateFlowResult;

    /// <summary>Nothing was created. Analyst named a reason instead of guessing (out-of-scope or
    /// genuinely ambiguous) — these are Analyst's own <see cref="ParsedIntent.Ambiguities"/>,
    /// verbatim, for the UI to render as the "clarifying question" state.</summary>
    public sealed record Ambiguous(IReadOnlyList<string> Ambiguities) : GenerateFlowResult;
}

public sealed class GenerateFlowCommandHandler : AuditableCommandHandler<GenerateFlowCommand, GenerateFlowResult>
{
    private readonly IAnalyst _analyst;
    private readonly ICoder _coder;
    private readonly IFlowRepository _flows;
    private readonly IPermissionChecker _permissions;

    public GenerateFlowCommandHandler(
        IAnalyst analyst, ICoder coder, IFlowRepository flows, IPermissionChecker permissions,
        IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _analyst = analyst;
        _coder = coder;
        _flows = flows;
        _permissions = permissions;
    }

    protected override async Task<GenerateFlowResult> HandleCore(GenerateFlowCommand cmd, CancellationToken ct)
    {
        // Same permission CreateFlowCommandHandler requires — generation ends in a persisted
        // Draft flow exactly like a manually-created one, so it needs the same authorization,
        // not a separate "AI generation" permission that would let the gate be bypassed by
        // pointing the UI at the manual endpoint instead.
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.FlowCreate, ct);

        var intent = await _analyst.AnalyzeAsync(cmd.Description, ct);
        if (intent.CandidateModuleKinds.Count == 0)
        {
            // Never calls Coder with nothing to build from — Analyst already refused to guess.
            return new GenerateFlowResult.Ambiguous(intent.Ambiguities);
        }

        var flow = await _coder.GenerateAsync(intent, cmd.TenantId, cmd.RequestedBy, ct);
        await _flows.Save(flow, ct);
        // FR-OPS-03: no commit here — AuditableCommandHandler.Handle commits this alongside the
        // audit entry, once, after HandleCore returns (same pattern every other handler here uses).

        // Every step the Coder generates shares one AiAttribution (D5-T6) — reading it off the
        // first step, rather than threading the attribution through a second return channel.
        var attribution = flow.Steps[0].AiAttribution!;
        return new GenerateFlowResult.Generated(flow.Id, attribution.Capability, attribution.Confidence, attribution.ModelIdentifier);
    }
}
