using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.ApiTesting;

public sealed record CreateApiCollectionCommand(string Name, string? Description, TenantId TenantId, UserId RequestedBy)
    : ICommand<ApiCollectionId>;

public sealed record RenameApiCollectionCommand(ApiCollectionId CollectionId, string Name, string? Description, UserId RequestedBy)
    : ICommand<bool>;

public sealed record DeleteApiCollectionCommand(ApiCollectionId CollectionId, UserId RequestedBy) : ICommand<bool>;

public sealed record AddApiRequestCommand(
    ApiCollectionId CollectionId, string Name, string Method, string Url,
    IReadOnlyDictionary<string, object?> Headers, IReadOnlyDictionary<string, object?> QueryParams,
    object? Body, object? Assertions, UserId RequestedBy) : ICommand<ApiRequestId>;

public sealed record UpdateApiRequestCommand(
    ApiCollectionId CollectionId, ApiRequestId RequestId, string Name, string Method, string Url,
    IReadOnlyDictionary<string, object?> Headers, IReadOnlyDictionary<string, object?> QueryParams,
    object? Body, object? Assertions, UserId RequestedBy) : ICommand<bool>;

public sealed record DeleteApiRequestCommand(ApiCollectionId CollectionId, ApiRequestId RequestId, UserId RequestedBy)
    : ICommand<bool>;

public sealed class CreateApiCollectionCommandHandler : AuditableCommandHandler<CreateApiCollectionCommand, ApiCollectionId>
{
    private readonly IApiCollectionRepository _collections;
    private readonly IPermissionChecker _permissions;

    public CreateApiCollectionCommandHandler(IApiCollectionRepository collections, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _collections = collections;
        _permissions = permissions;
    }

    protected override async Task<ApiCollectionId> HandleCore(CreateApiCollectionCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.ApiCollectionManage, ct);

        var collection = ApiCollection.Create(cmd.Name, cmd.Description, cmd.TenantId);
        await _collections.Save(collection, ct);
        return collection.Id;
    }
}

public sealed class RenameApiCollectionCommandHandler : AuditableCommandHandler<RenameApiCollectionCommand, bool>
{
    private readonly IApiCollectionRepository _collections;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public RenameApiCollectionCommandHandler(IApiCollectionRepository collections, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _collections = collections;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(RenameApiCollectionCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.ApiCollectionManage, ct);

        var collection = await _collections.GetById(cmd.CollectionId, ct)
            ?? throw new InvalidOperationException($"ApiCollection {cmd.CollectionId} not found.");
        _tenantGuard.EnsureAccessible(collection.TenantId);

        collection.Rename(cmd.Name, cmd.Description);
        await _collections.Save(collection, ct);
        return true;
    }
}

public sealed class DeleteApiCollectionCommandHandler : AuditableCommandHandler<DeleteApiCollectionCommand, bool>
{
    private readonly IApiCollectionRepository _collections;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public DeleteApiCollectionCommandHandler(IApiCollectionRepository collections, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _collections = collections;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(DeleteApiCollectionCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.ApiCollectionManage, ct);

        var collection = await _collections.GetById(cmd.CollectionId, ct)
            ?? throw new InvalidOperationException($"ApiCollection {cmd.CollectionId} not found.");
        _tenantGuard.EnsureAccessible(collection.TenantId);

        await _collections.Delete(collection, ct);
        return true;
    }
}

public sealed class AddApiRequestCommandHandler : AuditableCommandHandler<AddApiRequestCommand, ApiRequestId>
{
    private readonly IApiCollectionRepository _collections;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public AddApiRequestCommandHandler(IApiCollectionRepository collections, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _collections = collections;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<ApiRequestId> HandleCore(AddApiRequestCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.ApiCollectionManage, ct);

        var collection = await _collections.GetById(cmd.CollectionId, ct)
            ?? throw new InvalidOperationException($"ApiCollection {cmd.CollectionId} not found.");
        _tenantGuard.EnsureAccessible(collection.TenantId);

        var request = collection.AddRequest(cmd.Name, cmd.Method, cmd.Url, cmd.Headers, cmd.QueryParams, cmd.Body, cmd.Assertions);
        await _collections.Save(collection, ct);
        return request.Id;
    }
}

public sealed class UpdateApiRequestCommandHandler : AuditableCommandHandler<UpdateApiRequestCommand, bool>
{
    private readonly IApiCollectionRepository _collections;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public UpdateApiRequestCommandHandler(IApiCollectionRepository collections, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _collections = collections;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(UpdateApiRequestCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.ApiCollectionManage, ct);

        var collection = await _collections.GetById(cmd.CollectionId, ct)
            ?? throw new InvalidOperationException($"ApiCollection {cmd.CollectionId} not found.");
        _tenantGuard.EnsureAccessible(collection.TenantId);

        collection.UpdateRequest(cmd.RequestId, cmd.Name, cmd.Method, cmd.Url, cmd.Headers, cmd.QueryParams, cmd.Body, cmd.Assertions);
        await _collections.Save(collection, ct);
        return true;
    }
}

public sealed class DeleteApiRequestCommandHandler : AuditableCommandHandler<DeleteApiRequestCommand, bool>
{
    private readonly IApiCollectionRepository _collections;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public DeleteApiRequestCommandHandler(IApiCollectionRepository collections, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _collections = collections;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(DeleteApiRequestCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.ApiCollectionManage, ct);

        var collection = await _collections.GetById(cmd.CollectionId, ct)
            ?? throw new InvalidOperationException($"ApiCollection {cmd.CollectionId} not found.");
        _tenantGuard.EnsureAccessible(collection.TenantId);

        collection.RemoveRequest(cmd.RequestId);
        await _collections.Save(collection, ct);
        return true;
    }
}
