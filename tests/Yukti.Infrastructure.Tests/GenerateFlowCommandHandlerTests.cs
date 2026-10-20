using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.Ai;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T7: the English → Analyst → Coder → persisted Draft orchestration. Analyst and Coder are
/// faked here (each already has its own suite — D5-T4/T5/T6); what's under test is the handler's
/// own contract: permission first, ambiguity short-circuits before Coder, a generated flow is
/// actually persisted as Draft, and the attribution summary comes from the Coder's own steps.
/// Runs over the real in-memory repositories and the real <see cref="PermissionChecker"/>, the
/// same composition the CLI uses.
/// </summary>
public sealed class GenerateFlowCommandHandlerTests
{
    private sealed class FakeAnalyst : IAnalyst
    {
        private readonly ParsedIntent _intent;
        public FakeAnalyst(ParsedIntent intent) => _intent = intent;
        public Task<ParsedIntent> AnalyzeAsync(string description, TenantId tenantId, CancellationToken ct) => Task.FromResult(_intent);
    }

    private sealed class FakeCoder : ICoder
    {
        public int Calls { get; private set; }

        public Task<Flow> GenerateAsync(ParsedIntent intent, TenantId tenantId, UserId createdBy, CancellationToken ct)
        {
            Calls++;
            var flow = Flow.CreateDraft("Generated", null, tenantId, createdBy);
            flow.AddStep("Login", ModuleKind.Web, "navigate", new Dictionary<string, object?> { ["url"] = "https://x" },
                aiAttribution: new AiAttribution { Capability = "coder", Confidence = 0.6, ModelIdentifier = "stub", PromptVersion = "v1" });
            return Task.FromResult(flow);
        }
    }

    private sealed class FixedTenantContextAccessor : ITenantContextAccessor
    {
        public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
        public TenantId? CurrentTenantId { get; }
    }

    private sealed record Harness(GenerateFlowCommandHandler Handler, InMemoryFlowRepository Flows, FakeCoder Coder, TenantId TenantId, UserId AdminId, UserId NoRoleUserId);

    private static async Task<Harness> Build(ParsedIntent analystOutput)
    {
        var ct = CancellationToken.None;
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var flows = new InMemoryFlowRepository(uowFactory);
        var users = new InMemoryUserRepository(uowFactory);
        var roles = new InMemoryRoleRepository(uowFactory);
        var permissions = new PermissionChecker(users, roles);
        var hasher = new Pbkdf2PasswordHasher();
        var tenantId = TenantId.New();

        var adminRole = Role.CreateBaselineAdministrator();
        await roles.Save(adminRole, ct);
        var admin = User.Register("admin@yukti.local", "Admin", tenantId, hasher.Hash("unused"));
        admin.AssignRole(adminRole.Id);
        await users.Save(admin, ct);
        var noRole = User.Register("viewer@yukti.local", "No Role", tenantId, hasher.Hash("unused"));
        await users.Save(noRole, ct);
        await using (var uow = uowFactory.Create()) await uow.Commit(ct);

        var coder = new FakeCoder();
        var handler = new GenerateFlowCommandHandler(new FakeAnalyst(analystOutput), coder, flows, permissions,
            uowFactory, new InMemoryAuditRepository(), new FixedTenantContextAccessor(tenantId));
        return new Harness(handler, flows, coder, tenantId, admin.Id, noRole.Id);
    }

    [Fact]
    public async Task A_clear_request_persists_a_Draft_flow_and_returns_the_coders_own_attribution()
    {
        var h = await Build(new ParsedIntent(["web"], ["logs in"], []));

        var result = await h.Handler.Handle(new GenerateFlowCommand("log in", h.TenantId, h.AdminId), CancellationToken.None);

        var generated = Assert.IsType<GenerateFlowResult.Generated>(result);
        Assert.Equal("coder", generated.Capability);
        Assert.Equal(0.6, generated.Confidence);
        Assert.Equal("stub", generated.ModelIdentifier);

        var saved = await h.Flows.GetById(generated.FlowId, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(FlowStatus.Draft, saved!.Status);
        Assert.NotNull(saved.Steps[0].AiAttribution);
    }

    [Fact]
    public async Task An_ambiguous_request_returns_Analysts_own_reasons_and_never_calls_the_Coder()
    {
        var h = await Build(new ParsedIntent([], [], ["Could mean the api module or the web module."]));

        var result = await h.Handler.Handle(new GenerateFlowCommand("check it worked", h.TenantId, h.AdminId), CancellationToken.None);

        var ambiguous = Assert.IsType<GenerateFlowResult.Ambiguous>(result);
        Assert.Equal(["Could mean the api module or the web module."], ambiguous.Ambiguities);
        Assert.Equal(0, h.Coder.Calls);
    }

    [Fact]
    public async Task A_user_without_FlowCreate_is_refused_before_any_AI_work_happens()
    {
        var h = await Build(new ParsedIntent(["web"], [], []));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Handler.Handle(new GenerateFlowCommand("log in", h.TenantId, h.NoRoleUserId), CancellationToken.None));

        Assert.Equal(0, h.Coder.Calls);
    }
}
