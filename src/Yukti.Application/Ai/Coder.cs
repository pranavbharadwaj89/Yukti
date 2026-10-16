using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Ai;

/// <summary>
/// D5-T5: <see cref="ParsedIntent"/> → a Draft <see cref="Flow"/>, validated by Day 2's own
/// <see cref="Flow.Publish"/> (never a second copy of that logic) but always handed back in
/// <see cref="FlowStatus.Draft"/> — generation proposes, it never publishes. <paramref
/// name="tenantId"/>/<paramref name="createdBy"/> are the caller's own context (whoever asked
/// for a flow to be generated), never something the model supplies.
///
/// The concrete implementation (<c>Yukti.Infrastructure.Ai.Coder</c>) needs
/// <c>Yukti.Orchestration.IModuleRegistry</c> (to build the candidate modules' full
/// <c>ActionSchema</c> context, token-economy spec §R2) and <see cref="IModuleActionResolver"/>
/// (to actually run <see cref="Flow.Publish"/>) — both Infrastructure-layer dependencies, kept out
/// of this port the same way <see cref="IAnalyst"/> keeps <c>IModuleRegistry</c> out of itself.
/// </summary>
public interface ICoder
{
    Task<Flow> GenerateAsync(ParsedIntent intent, TenantId tenantId, UserId createdBy, CancellationToken ct);
}
