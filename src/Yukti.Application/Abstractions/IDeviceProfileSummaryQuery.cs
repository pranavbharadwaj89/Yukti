using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>Mobile Testing Module Spec, MOB-T2: the list a device-profile admin page's own table
/// needs — mirrors IRequestTemplateSummaryQuery's shape (a read model, never served off
/// IDeviceProfileRepository directly, FR-CQRS-01-style). Includes EndpointKind/Platform/DeviceName
/// since those are exactly what an admin picking a profile to edit or reference needs to see
/// without opening it.</summary>
public sealed record DeviceProfileSummary(
    DeviceProfileId Id, string Name, MobilePlatform Platform, string? DeviceName,
    DeviceEndpointKind EndpointKind, int Version);

public interface IDeviceProfileSummaryQuery
{
    Task<IReadOnlyList<DeviceProfileSummary>> ListByTenant(TenantId tenantId, CancellationToken ct);
}
