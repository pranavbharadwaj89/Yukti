using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

public sealed class EfApiCollectionSummaryQuery : IApiCollectionSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfApiCollectionSummaryQuery(YuktiDbContext context) => _context = context;

    // Owned collections (Requests) are always eagerly loaded by EF Core for
    // owned types — no explicit .Include needed, same as every other
    // aggregate's owned children in this codebase (FlowSteps, StepResults).
    public async Task<IReadOnlyList<ApiCollectionSummary>> ListByTenant(TenantId tenantId, CancellationToken ct)
    {
        var collections = await _context.ApiCollections
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        return collections
            .Select(c => new ApiCollectionSummary(
                c.Id, c.Name, c.Description,
                c.Requests.OrderBy(r => r.Order)
                    .Select(r => new ApiRequestSummary(r.Id, r.Name, r.Method, r.Url, r.Headers, r.QueryParams, r.Body, r.Assertions, r.Order))
                    .ToList()))
            .ToList();
    }
}
