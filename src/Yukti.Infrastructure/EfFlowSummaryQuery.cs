using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

public sealed class EfFlowSummaryQuery : IFlowSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfFlowSummaryQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<FlowSummaryReadModel>> ListByTenant(TenantId tenantId, CancellationToken ct) =>
        await _context.Flows
            .Where(f => f.TenantId == tenantId)
            .Select(f => new FlowSummaryReadModel(f.Id, f.Name, f.Status, f.Version))
            .ToListAsync(ct);
}
