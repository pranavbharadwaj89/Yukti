using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

/// <summary>
/// API Testing Module Spec, API-T9: "the UI shows which flows use a template." Loads the
/// tenant's flows (owned Steps are always eagerly loaded, same as every other owned collection
/// in this codebase) and filters for a `templateId` match client-side — `Params` is a jsonb
/// column EF Core can't filter by key server-side without raw SQL, and the tenant's own flow
/// count is small enough that this is a deliberate, documented simplicity-over-efficiency
/// tradeoff for a first version, not an oversight.
/// </summary>
public sealed class EfRequestTemplateUsageQuery : IRequestTemplateUsageQuery
{
    private readonly YuktiDbContext _context;
    public EfRequestTemplateUsageQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<RequestTemplateUsageReadModel>> FindFlowsReferencing(RequestTemplateId templateId, TenantId tenantId, CancellationToken ct)
    {
        var templateIdText = templateId.Value.ToString();
        var flows = await _context.Flows.Where(f => f.TenantId == tenantId).ToListAsync(ct);

        return flows
            .SelectMany(f => f.Steps
                .Where(s => s.Params.GetValueOrDefault("templateId") as string == templateIdText)
                .Select(s => new RequestTemplateUsageReadModel(f.Id, f.Name, s.Name)))
            .ToList();
    }
}
