using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T4: publish, run trigger, and role change each produce exactly one immutable audit entry
/// (Vol 1 Part IV §27). <c>AuditableCommandHandlerTests</c> already proves the generic
/// append-on-success/append-on-failure contract with a fake command; <c>AuditEntryGrantsTests</c>
/// proves immutability at the database-grant level (no UPDATE/DELETE reaches the table at all);
/// <c>IAuditRepository</c> itself exposes no Update/Delete method, so mutation is unreachable from
/// application code independent of either. This file is the one still missing: proving the same
/// guarantee end-to-end against the three specific, real operations the task names, through their
/// actual command handlers — never a stand-in command.
/// </summary>
public sealed class AuditTests
{
    private sealed class FixedTenantContextAccessor : ITenantContextAccessor
    {
        public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
        public TenantId? CurrentTenantId { get; }
    }

    private sealed record Harness(
        InMemoryUnitOfWorkFactory UowFactory, InMemoryAuditRepository Audit, ITenantContextAccessor TenantAccessor, ITenantGuard TenantGuard,
        InMemoryFlowRepository Flows, InMemoryFlowRunRepository FlowRuns, InMemoryModuleRegistrationRepository Modules,
        InMemoryUserRepository Users, InMemoryRoleRepository Roles, IPermissionChecker Permissions, TenantId TenantId, UserId AdminId);

    private static async Task<Harness> Build()
    {
        var ct = CancellationToken.None;
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var flows = new InMemoryFlowRepository(uowFactory);
        var flowRuns = new InMemoryFlowRunRepository(uowFactory);
        var modules = new InMemoryModuleRegistrationRepository(uowFactory);
        var users = new InMemoryUserRepository(uowFactory);
        var roles = new InMemoryRoleRepository(uowFactory);
        var audit = new InMemoryAuditRepository();
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

        await using (var uow = uowFactory.Create()) await uow.Commit(ct);

        return new Harness(uowFactory, audit, tenantAccessor, tenantGuard, flows, flowRuns, modules, users, roles, permissions, tenantId, admin.Id);
    }

    /// <summary>See AuthorizationTests' identical helper: InMemory*Repository.Save only stages a
    /// write until a unit of work commits — setup fixtures built directly via a repository need
    /// this explicitly, since no command handler is wrapping them here to commit on their behalf.</summary>
    private static async Task Commit(Harness h)
    {
        await using var uow = h.UowFactory.Create();
        await uow.Commit(CancellationToken.None);
    }

    [Fact]
    public async Task Publishing_a_flow_produces_exactly_one_audit_entry()
    {
        var h = await Build();
        var flow = Flow.CreateDraft("Audit target", null, h.TenantId, h.AdminId);
        await h.Flows.Save(flow, CancellationToken.None);
        await Commit(h);
        var resolver = new ModuleActionResolver(h.Modules);
        var handler = new PublishFlowCommandHandler(h.Flows, resolver, h.Permissions, h.TenantGuard, h.UowFactory, h.Audit, h.TenantAccessor);

        var result = await handler.Handle(new PublishFlowCommand(flow.Id, h.AdminId), CancellationToken.None);

        Assert.True(result.Succeeded);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(nameof(PublishFlowCommand), entry.CommandType);
        Assert.True(entry.Succeeded);
        Assert.Equal(h.TenantId, entry.TenantId);
    }

    [Fact]
    public async Task Triggering_a_flow_run_produces_exactly_one_audit_entry()
    {
        var h = await Build();
        var handler = new TriggerFlowRunCommandHandler(h.FlowRuns, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        await handler.Handle(new TriggerFlowRunCommand(FlowId.New(), RunTrigger.Api, null, h.TenantId, h.AdminId), CancellationToken.None);

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(nameof(TriggerFlowRunCommand), entry.CommandType);
        Assert.True(entry.Succeeded);
        Assert.Equal(h.TenantId, entry.TenantId);
    }

    [Fact]
    public async Task Changing_a_users_role_produces_exactly_one_audit_entry()
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
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(nameof(AssignRoleCommand), entry.CommandType);
        Assert.True(entry.Succeeded);
    }

    [Fact]
    public async Task A_failed_role_change_still_produces_exactly_one_audit_entry_recording_the_failure()
    {
        var h = await Build();
        var noRole = User.Register("norole@yukti.local", "No Role", h.TenantId, "unused");
        await h.Users.Save(noRole, CancellationToken.None);
        var target = User.Register("target2@yukti.local", "Target 2", h.TenantId, "unused");
        await h.Users.Save(target, CancellationToken.None);
        var flowAuthor = Role.CreateBaselineFlowAuthor();
        await h.Roles.Save(flowAuthor, CancellationToken.None);
        await Commit(h);
        var handler = new AssignRoleCommandHandler(h.Users, h.Permissions, h.UowFactory, h.Audit, h.TenantAccessor);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new AssignRoleCommand(target.Id, flowAuthor.Id, noRole.Id), CancellationToken.None));

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(nameof(AssignRoleCommand), entry.CommandType);
        Assert.False(entry.Succeeded);
        Assert.NotNull(entry.FailureReason);
    }
}
