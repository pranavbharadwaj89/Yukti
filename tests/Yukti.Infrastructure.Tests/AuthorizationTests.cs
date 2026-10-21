using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.ApiTesting;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Application.ModulePlugin;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T2: for every write command, an authorized-success and unauthorized-403 pair (Vol 1 §25.8).
/// FR-AUTHZ-02 says every command handler calls <see cref="IPermissionChecker.EnsurePermission"/>
/// as its first meaningful statement — proven here per command, not assumed from the one existing
/// precedent (<see cref="GenerateFlowCommandHandlerTests"/>'s own permission test, which this file
/// deliberately does not duplicate). <c>ForbiddenException</c> is what <see cref="PermissionChecker"/>
/// throws and what the API's global exception middleware maps 1:1 to HTTP 403
/// (<c>Program.cs</c>'s <c>catch (ForbiddenException ex)</c> branch) — the same exception-level proof
/// <see cref="TenantIsolationTests"/> uses for its own HTTP-adjacent guarantee.
///
/// <c>RegisterUserCommand</c> is deliberately excluded: it is open self-registration by design (no
/// caller identity exists yet to check a permission against), so there is no unauthorized case to
/// prove for it.
///
/// Every unauthorized case additionally confirms no mutation happened — permission is checked
/// before any resource is loaded or changed in every handler, so a thrown <c>ForbiddenException</c>
/// already guarantees this structurally; the re-fetch assertions below prove it empirically too,
/// the same rigor <see cref="TenantIsolationTests"/> applies to its own raw-query checks.
/// </summary>
public sealed class AuthorizationTests
{
    private sealed class FixedTenantContextAccessor : ITenantContextAccessor
    {
        public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
        public TenantId? CurrentTenantId { get; }
    }

    /// <summary>No production in-memory implementation of <see cref="IApiCollectionRepository"/>
    /// exists (only the real EF one) — a minimal test double, same role as every other
    /// <c>InMemory*Repository</c> this file reuses from <c>Yukti.Infrastructure.InMemory</c>.</summary>
    private sealed class StubApiCollectionRepository : IApiCollectionRepository
    {
        private readonly Dictionary<Guid, ApiCollection> _store = new();

        public Task<ApiCollection?> GetById(ApiCollectionId id, CancellationToken ct) =>
            Task.FromResult(_store.TryGetValue(id.Value, out var collection) ? collection : null);

        public Task Save(ApiCollection collection, CancellationToken ct)
        {
            _store[collection.Id.Value] = collection;
            return Task.CompletedTask;
        }

        public Task Delete(ApiCollection collection, CancellationToken ct)
        {
            _store.Remove(collection.Id.Value);
            return Task.CompletedTask;
        }
    }

    private sealed record Harness(
        InMemoryUnitOfWorkFactory UowFactory, IAuditRepository Audit, ITenantContextAccessor TenantAccessor, ITenantGuard TenantGuard,
        InMemoryFlowRepository Flows, InMemoryFlowRunRepository FlowRuns, InMemoryModuleRegistrationRepository Modules,
        InMemoryUserRepository Users, InMemoryRoleRepository Roles, StubApiCollectionRepository ApiCollections,
        IPermissionChecker Permissions, TenantId TenantId, UserId AdminId, UserId NoRoleUserId);

    private static async Task<Harness> Build()
    {
        var ct = CancellationToken.None;
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var flows = new InMemoryFlowRepository(uowFactory);
        var flowRuns = new InMemoryFlowRunRepository(uowFactory);
        var modules = new InMemoryModuleRegistrationRepository(uowFactory);
        var users = new InMemoryUserRepository(uowFactory);
        var roles = new InMemoryRoleRepository(uowFactory);
        var apiCollections = new StubApiCollectionRepository();
        var permissions = new PermissionChecker(users, roles);
        var hasher = new Pbkdf2PasswordHasher();
        var tenantId = TenantId.New();
        var tenantAccessor = new FixedTenantContextAccessor(tenantId);
        var tenantGuard = new TenantGuard(tenantAccessor);

        var adminRole = Role.CreateBaselineAdministrator();
        await roles.Save(adminRole, ct);
        var admin = User.Register("admin@yukti.local", "Admin", tenantId, hasher.Hash("unused"));
        admin.AssignRole(adminRole.Id);
        await users.Save(admin, ct);

        var noRole = User.Register("viewer@yukti.local", "No Role", tenantId, hasher.Hash("unused"));
        await users.Save(noRole, ct);

        await using (var uow = uowFactory.Create()) await uow.Commit(ct);

        return new Harness(uowFactory, new InMemoryAuditRepository(), tenantAccessor, tenantGuard,
            flows, flowRuns, modules, users, roles, apiCollections, permissions, tenantId, admin.Id, noRole.Id);
    }

    /// <summary>The InMemory*Repository.Save calls above only stage a write — <see cref="InMemoryUnitOfWork.Commit"/>
    /// is what actually applies it to the backing store. Setup fixtures built directly via a repository
    /// (bypassing a command handler, which commits its own staged work on success) need this explicitly.</summary>
    private static async Task Commit(Harness h)
    {
        await using var uow = h.UowFactory.Create();
        await uow.Commit(CancellationToken.None);
    }

    // ---------- IdentityAccess: AssignRoleCommand (UserManage) ----------

    [Fact]
    public async Task AssignRole_succeeds_for_a_user_holding_UserManage()
    {
        var h = await Build();
        var target = User.Register("target@yukti.local", "Target", h.TenantId, "unused");
        await h.Users.Save(target, CancellationToken.None);
        var flowAuthor = Role.CreateBaselineFlowAuthor();
        await h.Roles.Save(flowAuthor, CancellationToken.None);
        await Commit(h);
        var handler = new AssignRoleCommandHandler(h.Users, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new AssignRoleCommand(target.Id, flowAuthor.Id, h.AdminId), CancellationToken.None);

        Assert.True(result);
        var reloaded = await h.Users.GetById(target.Id, CancellationToken.None);
        Assert.Contains(flowAuthor.Id, reloaded!.RoleIds);
    }

    [Fact]
    public async Task AssignRole_is_refused_with_403_for_a_user_lacking_UserManage()
    {
        var h = await Build();
        var target = User.Register("target@yukti.local", "Target", h.TenantId, "unused");
        await h.Users.Save(target, CancellationToken.None);
        var flowAuthor = Role.CreateBaselineFlowAuthor();
        await h.Roles.Save(flowAuthor, CancellationToken.None);
        await Commit(h);
        var handler = new AssignRoleCommandHandler(h.Users, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new AssignRoleCommand(target.Id, flowAuthor.Id, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.Users.GetById(target.Id, CancellationToken.None);
        Assert.DoesNotContain(flowAuthor.Id, reloaded!.RoleIds);
    }

    // ---------- IdentityAccess: UpdateRolePermissionsCommand (UserManage) ----------

    [Fact]
    public async Task UpdateRolePermissions_succeeds_for_a_user_holding_UserManage()
    {
        var h = await Build();
        var role = Role.CreateBaselineFlowRunner();
        await h.Roles.Save(role, CancellationToken.None);
        await Commit(h);
        var handler = new UpdateRolePermissionsCommandHandler(h.Roles, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        var newVersion = await handler.Handle(
            new UpdateRolePermissionsCommand(role.Id, new HashSet<Permission> { Permission.FlowCreate }, h.AdminId), CancellationToken.None);

        Assert.Equal(2, newVersion);
        var reloaded = await h.Roles.GetById(role.Id, CancellationToken.None);
        Assert.True(reloaded!.Has(Permission.FlowCreate));
        Assert.False(reloaded.Has(Permission.FlowExecute));
    }

    [Fact]
    public async Task UpdateRolePermissions_is_refused_with_403_for_a_user_lacking_UserManage()
    {
        var h = await Build();
        var role = Role.CreateBaselineFlowRunner();
        await h.Roles.Save(role, CancellationToken.None);
        await Commit(h);
        var handler = new UpdateRolePermissionsCommandHandler(h.Roles, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new UpdateRolePermissionsCommand(role.Id, new HashSet<Permission> { Permission.FlowCreate }, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.Roles.GetById(role.Id, CancellationToken.None);
        Assert.Equal(1, reloaded!.Version);
        Assert.True(reloaded.Has(Permission.FlowExecute));
    }

    // ---------- FlowAuthoring: CreateFlowCommand (FlowCreate) ----------

    [Fact]
    public async Task CreateFlow_succeeds_for_a_user_holding_FlowCreate()
    {
        var h = await Build();
        var handler = new CreateFlowCommandHandler(h.Flows, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        var flowId = await handler.Handle(new CreateFlowCommand("Authorized flow", null, h.TenantId, h.AdminId), CancellationToken.None);

        var reloaded = await h.Flows.GetById(flowId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("Authorized flow", reloaded!.Name);
    }

    [Fact]
    public async Task CreateFlow_is_refused_with_403_for_a_user_lacking_FlowCreate()
    {
        var h = await Build();
        var handler = new CreateFlowCommandHandler(h.Flows, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        // The permission check is the handler's first statement, before any Flow aggregate is even
        // constructed — there is structurally nothing to query back for "was it created," unlike
        // the other commands below that mutate a pre-existing aggregate.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateFlowCommand("Unauthorized flow", null, h.TenantId, h.NoRoleUserId), CancellationToken.None));
    }

    // ---------- FlowAuthoring: AddFlowStepCommand (FlowEdit) ----------

    [Fact]
    public async Task AddFlowStep_succeeds_for_a_user_holding_FlowEdit()
    {
        var h = await Build();
        var flow = Flow.CreateDraft("Step target", null, h.TenantId, h.AdminId);
        await h.Flows.Save(flow, CancellationToken.None);
        await Commit(h);
        var handler = new AddFlowStepCommandHandler(h.Flows, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(
            new AddFlowStepCommand(flow.Id, "Step 1", ModuleKind.Web, "navigate", new Dictionary<string, object?> { ["url"] = "https://x" }, null, null, h.AdminId),
            CancellationToken.None);

        Assert.True(result);
        var reloaded = await h.Flows.GetById(flow.Id, CancellationToken.None);
        Assert.Single(reloaded!.Steps);
    }

    [Fact]
    public async Task AddFlowStep_is_refused_with_403_for_a_user_lacking_FlowEdit()
    {
        var h = await Build();
        var flow = Flow.CreateDraft("Step target", null, h.TenantId, h.AdminId);
        await h.Flows.Save(flow, CancellationToken.None);
        await Commit(h);
        var handler = new AddFlowStepCommandHandler(h.Flows, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new AddFlowStepCommand(flow.Id, "Step 1", ModuleKind.Web, "navigate", new Dictionary<string, object?> { ["url"] = "https://x" }, null, null, h.NoRoleUserId),
                CancellationToken.None));

        var reloaded = await h.Flows.GetById(flow.Id, CancellationToken.None);
        Assert.Empty(reloaded!.Steps);
    }

    // ---------- FlowAuthoring: PublishFlowCommand (FlowPublish) ----------

    [Fact]
    public async Task PublishFlow_succeeds_for_a_user_holding_FlowPublish()
    {
        var h = await Build();
        var flow = Flow.CreateDraft("Publish target", null, h.TenantId, h.AdminId);
        await h.Flows.Save(flow, CancellationToken.None);
        await Commit(h);
        var resolver = new ModuleActionResolver(h.Modules);
        var handler = new PublishFlowCommandHandler(h.Flows, resolver, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new PublishFlowCommand(flow.Id, h.AdminId), CancellationToken.None);

        Assert.True(result.Succeeded);
        var reloaded = await h.Flows.GetById(flow.Id, CancellationToken.None);
        Assert.Equal(FlowStatus.Published, reloaded!.Status);
    }

    [Fact]
    public async Task PublishFlow_is_refused_with_403_for_a_user_lacking_FlowPublish()
    {
        var h = await Build();
        var flow = Flow.CreateDraft("Publish target", null, h.TenantId, h.AdminId);
        await h.Flows.Save(flow, CancellationToken.None);
        await Commit(h);
        var resolver = new ModuleActionResolver(h.Modules);
        var handler = new PublishFlowCommandHandler(h.Flows, resolver, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new PublishFlowCommand(flow.Id, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.Flows.GetById(flow.Id, CancellationToken.None);
        Assert.Equal(FlowStatus.Draft, reloaded!.Status);
    }

    // ---------- Execution: TriggerFlowRunCommand (FlowExecute) ----------

    [Fact]
    public async Task TriggerFlowRun_succeeds_for_a_user_holding_FlowExecute()
    {
        var h = await Build();
        var handler = new TriggerFlowRunCommandHandler(h.FlowRuns, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);
        var flowId = FlowId.New();

        var runId = await handler.Handle(new TriggerFlowRunCommand(flowId, RunTrigger.Api, null, h.TenantId, h.AdminId), CancellationToken.None);

        var reloaded = await h.FlowRuns.GetById(runId, CancellationToken.None);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task TriggerFlowRun_is_refused_with_403_for_a_user_lacking_FlowExecute()
    {
        var h = await Build();
        var handler = new TriggerFlowRunCommandHandler(h.FlowRuns, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);
        var flowId = FlowId.New();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new TriggerFlowRunCommand(flowId, RunTrigger.Api, null, h.TenantId, h.NoRoleUserId), CancellationToken.None));
    }

    // ---------- Execution: CancelFlowRunCommand (FlowExecute) ----------

    [Fact]
    public async Task CancelFlowRun_succeeds_for_a_user_holding_FlowExecute()
    {
        var h = await Build();
        var run = FlowRun.Create(FlowId.New(), RunTrigger.Api, h.TenantId);
        await h.FlowRuns.Save(run, CancellationToken.None);
        await Commit(h);
        var handler = new CancelFlowRunCommandHandler(h.FlowRuns, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new CancelFlowRunCommand(run.Id, h.AdminId), CancellationToken.None);

        Assert.True(result);
        var reloaded = await h.FlowRuns.GetById(run.Id, CancellationToken.None);
        Assert.Equal(RunStatus.Cancelled, reloaded!.Status);
    }

    [Fact]
    public async Task CancelFlowRun_is_refused_with_403_for_a_user_lacking_FlowExecute()
    {
        var h = await Build();
        var run = FlowRun.Create(FlowId.New(), RunTrigger.Api, h.TenantId);
        await h.FlowRuns.Save(run, CancellationToken.None);
        await Commit(h);
        var handler = new CancelFlowRunCommandHandler(h.FlowRuns, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CancelFlowRunCommand(run.Id, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.FlowRuns.GetById(run.Id, CancellationToken.None);
        Assert.Equal(RunStatus.Pending, reloaded!.Status);
    }

    // ---------- ModulePlugin: RegisterModuleCommand (ModuleManage) ----------

    [Fact]
    public async Task RegisterModule_succeeds_for_a_user_holding_ModuleManage()
    {
        var h = await Build();
        var handler = new RegisterModuleCommandHandler(h.Modules, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);
        var action = new ActionSchema { ActionName = "doThing", Parameters = Array.Empty<ParamSpec>(), Description = "d" };

        var registrationId = await handler.Handle(
            new RegisterModuleCommand(ModuleKind.Custom("authz-test"), "Authz Test Module", TrustTier.Community, new[] { action }, "1.0", h.AdminId),
            CancellationToken.None);

        var reloaded = await h.Modules.GetById(registrationId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.HasAction("doThing"));
    }

    [Fact]
    public async Task RegisterModule_is_refused_with_403_for_a_user_lacking_ModuleManage()
    {
        var h = await Build();
        var handler = new RegisterModuleCommandHandler(h.Modules, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);
        var action = new ActionSchema { ActionName = "doThing", Parameters = Array.Empty<ParamSpec>(), Description = "d" };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new RegisterModuleCommand(ModuleKind.Custom("authz-test-2"), "Authz Test Module", TrustTier.Community, new[] { action }, "1.0", h.NoRoleUserId),
                CancellationToken.None));
    }

    // ---------- ApiTesting: CreateApiCollectionCommand (ApiCollectionManage) ----------

    [Fact]
    public async Task CreateApiCollection_succeeds_for_a_user_holding_ApiCollectionManage()
    {
        var h = await Build();
        var handler = new CreateApiCollectionCommandHandler(h.ApiCollections, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        var collectionId = await handler.Handle(new CreateApiCollectionCommand("Authorized collection", null, h.TenantId, h.AdminId), CancellationToken.None);

        var reloaded = await h.ApiCollections.GetById(collectionId, CancellationToken.None);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task CreateApiCollection_is_refused_with_403_for_a_user_lacking_ApiCollectionManage()
    {
        var h = await Build();
        var handler = new CreateApiCollectionCommandHandler(h.ApiCollections, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateApiCollectionCommand("Unauthorized collection", null, h.TenantId, h.NoRoleUserId), CancellationToken.None));
    }

    // ---------- ApiTesting: RenameApiCollectionCommand (ApiCollectionManage) ----------

    [Fact]
    public async Task RenameApiCollection_succeeds_for_a_user_holding_ApiCollectionManage()
    {
        var h = await Build();
        var collection = ApiCollection.Create("Original", null, h.TenantId);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new RenameApiCollectionCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new RenameApiCollectionCommand(collection.Id, "Renamed", null, h.AdminId), CancellationToken.None);

        Assert.True(result);
        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Equal("Renamed", reloaded!.Name);
    }

    [Fact]
    public async Task RenameApiCollection_is_refused_with_403_for_a_user_lacking_ApiCollectionManage()
    {
        var h = await Build();
        var collection = ApiCollection.Create("Original", null, h.TenantId);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new RenameApiCollectionCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RenameApiCollectionCommand(collection.Id, "Renamed", null, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Equal("Original", reloaded!.Name);
    }

    // ---------- ApiTesting: DeleteApiCollectionCommand (ApiCollectionManage) ----------

    [Fact]
    public async Task DeleteApiCollection_succeeds_for_a_user_holding_ApiCollectionManage()
    {
        var h = await Build();
        var collection = ApiCollection.Create("To delete", null, h.TenantId);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new DeleteApiCollectionCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new DeleteApiCollectionCommand(collection.Id, h.AdminId), CancellationToken.None);

        Assert.True(result);
        Assert.Null(await h.ApiCollections.GetById(collection.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteApiCollection_is_refused_with_403_for_a_user_lacking_ApiCollectionManage()
    {
        var h = await Build();
        var collection = ApiCollection.Create("To delete", null, h.TenantId);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new DeleteApiCollectionCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new DeleteApiCollectionCommand(collection.Id, h.NoRoleUserId), CancellationToken.None));

        Assert.NotNull(await h.ApiCollections.GetById(collection.Id, CancellationToken.None));
    }

    // ---------- ApiTesting: AddApiRequestCommand (ApiCollectionManage) ----------

    [Fact]
    public async Task AddApiRequest_succeeds_for_a_user_holding_ApiCollectionManage()
    {
        var h = await Build();
        var collection = ApiCollection.Create("Requests", null, h.TenantId);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new AddApiRequestCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);
        var empty = new Dictionary<string, object?>();

        var requestId = await handler.Handle(
            new AddApiRequestCommand(collection.Id, "GET it", "GET", "https://x/y", empty, empty, null, null, h.AdminId), CancellationToken.None);

        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Single(reloaded!.Requests);
        Assert.Equal(requestId, reloaded.Requests[0].Id);
    }

    [Fact]
    public async Task AddApiRequest_is_refused_with_403_for_a_user_lacking_ApiCollectionManage()
    {
        var h = await Build();
        var collection = ApiCollection.Create("Requests", null, h.TenantId);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new AddApiRequestCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);
        var empty = new Dictionary<string, object?>();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new AddApiRequestCommand(collection.Id, "GET it", "GET", "https://x/y", empty, empty, null, null, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Empty(reloaded!.Requests);
    }

    // ---------- ApiTesting: UpdateApiRequestCommand (ApiCollectionManage) ----------

    [Fact]
    public async Task UpdateApiRequest_succeeds_for_a_user_holding_ApiCollectionManage()
    {
        var h = await Build();
        var empty = new Dictionary<string, object?>();
        var collection = ApiCollection.Create("Requests", null, h.TenantId);
        var request = collection.AddRequest("Original", "GET", "https://x/y", empty, empty, null, null);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new UpdateApiRequestCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(
            new UpdateApiRequestCommand(collection.Id, request.Id, "Renamed", "POST", "https://x/z", empty, empty, null, null, h.AdminId), CancellationToken.None);

        Assert.True(result);
        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Equal("Renamed", reloaded!.Requests[0].Name);
        Assert.Equal("POST", reloaded.Requests[0].Method);
    }

    [Fact]
    public async Task UpdateApiRequest_is_refused_with_403_for_a_user_lacking_ApiCollectionManage()
    {
        var h = await Build();
        var empty = new Dictionary<string, object?>();
        var collection = ApiCollection.Create("Requests", null, h.TenantId);
        var request = collection.AddRequest("Original", "GET", "https://x/y", empty, empty, null, null);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new UpdateApiRequestCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new UpdateApiRequestCommand(collection.Id, request.Id, "Renamed", "POST", "https://x/z", empty, empty, null, null, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Equal("Original", reloaded!.Requests[0].Name);
        Assert.Equal("GET", reloaded.Requests[0].Method);
    }

    // ---------- ApiTesting: DeleteApiRequestCommand (ApiCollectionManage) ----------

    [Fact]
    public async Task DeleteApiRequest_succeeds_for_a_user_holding_ApiCollectionManage()
    {
        var h = await Build();
        var empty = new Dictionary<string, object?>();
        var collection = ApiCollection.Create("Requests", null, h.TenantId);
        var request = collection.AddRequest("To delete", "GET", "https://x/y", empty, empty, null, null);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new DeleteApiRequestCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new DeleteApiRequestCommand(collection.Id, request.Id, h.AdminId), CancellationToken.None);

        Assert.True(result);
        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Empty(reloaded!.Requests);
    }

    [Fact]
    public async Task DeleteApiRequest_is_refused_with_403_for_a_user_lacking_ApiCollectionManage()
    {
        var h = await Build();
        var empty = new Dictionary<string, object?>();
        var collection = ApiCollection.Create("Requests", null, h.TenantId);
        var request = collection.AddRequest("To delete", "GET", "https://x/y", empty, empty, null, null);
        await h.ApiCollections.Save(collection, CancellationToken.None);
        var handler = new DeleteApiRequestCommandHandler(h.ApiCollections, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new DeleteApiRequestCommand(collection.Id, request.Id, h.NoRoleUserId), CancellationToken.None));

        var reloaded = await h.ApiCollections.GetById(collection.Id, CancellationToken.None);
        Assert.Single(reloaded!.Requests);
    }
}
