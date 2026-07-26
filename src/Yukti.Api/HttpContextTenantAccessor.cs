using Microsoft.AspNetCore.Http;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Api;

/// <summary>
/// FR-TENANT-02's concrete source of truth: reads the "tenant" claim off
/// the current request's authenticated principal, the same claim
/// ClaimsPrincipalExtensions.GetTenantId() already reads in Program.cs —
/// this class exists so Application-layer code (repositories, TenantGuard)
/// can depend on the abstraction without a reference to ASP.NET Core.
/// Never reads a header, query param, or anything client-supplied outside
/// the JWT itself.
/// </summary>
public sealed class HttpContextTenantAccessor : ITenantContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextTenantAccessor(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public TenantId? CurrentTenantId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User.FindFirst("tenant")?.Value;
            return Guid.TryParse(claim, out var guid) ? new TenantId(guid) : null;
        }
    }
}
