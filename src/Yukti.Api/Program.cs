using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Yukti.Api;
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Application.ModulePlugin;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

var builder = WebApplication.CreateBuilder(args);

// ---- JWT signing key ----
// Generated once at process startup — the same kind of documented,
// temporary secret-management shortcut as InMemoryCredentialResolver: a
// real deployment needs a persisted/rotatable key (Vault or similar), or
// every restart invalidates every outstanding access token. Swapping this
// in later requires no change to JwtTokenService's public surface, only
// how these bytes are sourced.
var signingKey = RandomNumberGenerator.GetBytes(32);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core's default inbound claim-type map silently
        // rewrites short claim names like "sub" to long legacy XML-namespace
        // URIs (ClaimTypes.NameIdentifier) before the token ever reaches a
        // handler — FindFirstValue("sub") then returns null even though the
        // JWT genuinely carries a sub claim. Found by testing this live: the
        // first authenticated POST after login threw ArgumentNullException
        // inside Guid.Parse in ClaimsPrincipalExtensions.GetUserId.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// ---- Composition root ----
// Mirrors Yukti.Host's manual wiring — no DI container abstraction needed,
// only the built-in ASP.NET Core container. All infra here is the same
// demo-grade in-memory implementation Yukti.Host uses; swapping to a real
// EF Core + PostgreSQL Yukti.Infrastructure project (once DB access is
// available) means registering different implementations of the same
// repository / IUnitOfWorkFactory interfaces here — no other file in this
// project changes.
builder.Services.AddSingleton<InMemoryDomainEventDispatcher>();
builder.Services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<InMemoryDomainEventDispatcher>());
builder.Services.AddSingleton<InMemoryUnitOfWorkFactory>();
builder.Services.AddSingleton<IUnitOfWorkFactory>(sp => sp.GetRequiredService<InMemoryUnitOfWorkFactory>());

builder.Services.AddSingleton<IFlowRepository, InMemoryFlowRepository>();
builder.Services.AddSingleton<IFlowRunRepository, InMemoryFlowRunRepository>();
builder.Services.AddSingleton<IModuleRegistrationRepository, InMemoryModuleRegistrationRepository>();
builder.Services.AddSingleton<IModuleActionResolver, ModuleActionResolver>();
builder.Services.AddSingleton<ICredentialResolver, InMemoryCredentialResolver>();

builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IRoleRepository, InMemoryRoleRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService>(_ => new JwtTokenService(signingKey));
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();

builder.Services.AddSingleton<ApiModule>();
builder.Services.AddSingleton<LogsModule>();
builder.Services.AddSingleton<IModuleRegistry>(sp =>
{
    var registry = new ModuleRegistry();
    registry.Register(sp.GetRequiredService<ApiModule>());
    registry.Register(sp.GetRequiredService<LogsModule>());
    return registry;
});
builder.Services.AddSingleton<IModuleDispatcher, ModuleDispatcher>();
builder.Services.AddSingleton<IVariableStore, VariableStore>();
builder.Services.AddSingleton<IRetryFlakeHandler, RetryFlakeHandler>();
builder.Services.AddSingleton<FlowEngine>();

builder.Services.AddScoped<CreateFlowCommandHandler>();
builder.Services.AddScoped<AddFlowStepCommandHandler>();
builder.Services.AddScoped<PublishFlowCommandHandler>();
builder.Services.AddScoped<TriggerFlowRunCommandHandler>();
builder.Services.AddScoped<CancelFlowRunCommandHandler>();
builder.Services.AddScoped<RegisterModuleCommandHandler>();
builder.Services.AddScoped<RegisterUserCommandHandler>();
builder.Services.AddScoped<AssignRoleCommandHandler>();
builder.Services.AddScoped<UpdateRolePermissionsCommandHandler>();

var app = builder.Build();

// ---- Global exception -> HTTP status mapping ----
// Keeps every endpoint below free of repeated try/catch for the two
// cross-cutting failure modes every authorized command can now raise:
// ForbiddenException (FR-AUTHZ-02's EnsurePermission) and DomainException
// (any aggregate invariant). Endpoint-local catches still handle
// "not found" (InvalidOperationException), which needs a different message
// per resource type.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ForbiddenException ex)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (DomainException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.UseAuthentication();
app.UseAuthorization();

// ---- Seed baseline roles + built-in modules + one bootstrap admin ----
// Flow.Publish validates every step's (module, action) against a
// registered ModuleRegistration (Volume 1 Part II §9.2), and every command
// now calls EnsurePermission (FR-AUTHZ-02) — without a first user holding
// UserManage, nobody could ever grant anybody else a role. Both gaps are
// closed the same way: direct aggregate construction + repository save at
// startup, bypassing the command/permission pipeline entirely, the same
// pattern Yukti.Host's smoke test already uses for module registration.
// The seeded admin's password is a documented dev-only default, not a
// secret meant to survive into any real deployment.
RoleId adminRoleId, authorRoleId, runnerRoleId;
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
    var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var uowFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

    var admin = Role.CreateBaselineAdministrator();
    var author = Role.CreateBaselineFlowAuthor();
    var runner = Role.CreateBaselineFlowRunner();
    await roles.Save(admin, default);
    await roles.Save(author, default);
    await roles.Save(runner, default);
    adminRoleId = admin.Id;
    authorRoleId = author.Id;
    runnerRoleId = runner.Id;

    var bootstrapTenant = TenantId.New();
    var bootstrapAdmin = Yukti.Domain.IdentityAccess.User.Register(
        "admin@yukti.local", "Bootstrap Administrator", bootstrapTenant, hasher.Hash("ChangeMe123!"));
    bootstrapAdmin.AssignRole(adminRoleId);
    await users.Save(bootstrapAdmin, default);

    await using var uow = uowFactory.Create();
    await uow.Commit(default);

    var registerHandler = scope.ServiceProvider.GetRequiredService<RegisterModuleCommandHandler>();
    var apiModule = scope.ServiceProvider.GetRequiredService<ApiModule>();
    var logsModule = scope.ServiceProvider.GetRequiredService<LogsModule>();

    await registerHandler.Handle(new RegisterModuleCommand(
        ModuleKind.Api, "API Automation", TrustTier.BuiltIn, apiModule.GetSupportedActions(), apiModule.ContractVersion, bootstrapAdmin.Id), default);
    await registerHandler.Handle(new RegisterModuleCommand(
        ModuleKind.Logs, "Log Automation", TrustTier.BuiltIn, logsModule.GetSupportedActions(), logsModule.ContractVersion, bootstrapAdmin.Id), default);
}

const string flowNotFound = "Flow not found.";
const string runNotFound = "FlowRun not found.";
const string invalidCredentials = "Invalid email or password."; // FR-AUTH-05: identical for "no such user" and "wrong password"

// ---- Auth endpoints ----
var auth = app.MapGroup("/api/auth").WithTags("Auth");

// Open self-registration, defaulting to the Flow Author baseline role in a
// brand-new tenant (pooled multi-tenancy's signup path — FR-TENANT-03).
// Real deployments may want to gate this differently (invite-only, SSO
// provisioning); left open here since there is no other way to reach a
// second user without an existing Administrator.
auth.MapPost("/register", async (RegisterRequest req, RegisterUserCommandHandler handler, CancellationToken ct) =>
{
    var userId = await handler.Handle(
        new RegisterUserCommand(req.Email, req.Password, req.DisplayName, TenantId.New(), authorRoleId), ct);
    return Results.Created($"/api/auth/users/{userId.Value}", new { userId = userId.Value });
});

auth.MapPost("/login", async (
    LoginRequest req, IUserRepository users, IRoleRepository roleRepo,
    IPasswordHasher hasher, IJwtTokenService jwt, IRefreshTokenStore refreshTokens, CancellationToken ct) =>
{
    var user = await users.GetByEmail(req.Email, ct);
    if (user is null || !hasher.Verify(req.Password, user.PasswordHash))
        return Results.Json(new { error = invalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

    var roleList = new List<Role>();
    foreach (var roleId in user.RoleIds)
    {
        var role = await roleRepo.GetById(roleId, ct);
        if (role is not null) roleList.Add(role);
    }

    var access = jwt.IssueAccessToken(user, roleList);
    var refresh = await refreshTokens.Issue(user.Id, ct);
    return Results.Ok(new TokenResponse(access.Value, access.ExpiresAt, refresh));
});

auth.MapPost("/refresh", async (
    RefreshRequest req, IRefreshTokenStore refreshTokens, IUserRepository users, IRoleRepository roleRepo,
    IJwtTokenService jwt, CancellationToken ct) =>
{
    var userId = await refreshTokens.Consume(req.RefreshToken, ct);
    if (userId is null)
        return Results.Json(new { error = "Refresh token is invalid, expired, or already used." }, statusCode: StatusCodes.Status401Unauthorized);

    var user = await users.GetById(userId.Value, ct);
    if (user is null)
        return Results.Json(new { error = invalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

    var roleList = new List<Role>();
    foreach (var roleId in user.RoleIds)
    {
        var role = await roleRepo.GetById(roleId, ct);
        if (role is not null) roleList.Add(role);
    }

    var access = jwt.IssueAccessToken(user, roleList);
    var newRefresh = await refreshTokens.Issue(user.Id, ct); // rotation: old token already consumed above
    return Results.Ok(new TokenResponse(access.Value, access.ExpiresAt, newRefresh));
});

// Admin-only: exercises FR-AUTHZ-04 directly — bumping a role's Version
// here means every user holding that role is re-evaluated against the new
// permission set on their very next request, token or no token.
app.MapPost("/api/roles/{roleId:guid}/permissions", async (
    Guid roleId, UpdateRolePermissionsRequest req, ClaimsPrincipal principal,
    UpdateRolePermissionsCommandHandler handler, CancellationToken ct) =>
{
    var permissions = req.Permissions.Select(p => Enum.Parse<Permission>(p, ignoreCase: true)).ToHashSet();
    var newVersion = await handler.Handle(
        new UpdateRolePermissionsCommand(new RoleId(roleId), permissions, principal.GetUserId()), ct);
    return Results.Ok(new { roleId, version = newVersion });
}).RequireAuthorization().WithTags("Auth");

// ---- Flow / Run / Module endpoints — all require authentication now ----
var flows = app.MapGroup("/api/flows").WithTags("Flows").RequireAuthorization();

flows.MapPost("/", async (CreateFlowRequest req, ClaimsPrincipal principal, CreateFlowCommandHandler handler, CancellationToken ct) =>
{
    var flowId = await handler.Handle(new CreateFlowCommand(req.Name, req.Description, principal.GetTenantId(), principal.GetUserId()), ct);
    return Results.Created($"/api/flows/{flowId.Value}", new { flowId = flowId.Value });
});

flows.MapGet("/{flowId:guid}", async (Guid flowId, IFlowRepository repo, CancellationToken ct) =>
{
    var flow = await repo.GetById(new FlowId(flowId), ct);
    return flow is null ? Results.NotFound(new { error = flowNotFound }) : Results.Ok(FlowResponse.From(flow));
});

flows.MapPost("/{flowId:guid}/steps", async (Guid flowId, AddStepRequest req, ClaimsPrincipal principal, AddFlowStepCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        await handler.Handle(new AddFlowStepCommand(
            new FlowId(flowId), req.Name, ModuleKind.Custom(req.Module), req.Action,
            JsonParamNormalizer.Normalize(req.Params), req.SaveAs, req.When, principal.GetUserId()), ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new { error = flowNotFound });
    }
});

flows.MapPost("/{flowId:guid}/publish", async (Guid flowId, ClaimsPrincipal principal, PublishFlowCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        var result = await handler.Handle(new PublishFlowCommand(new FlowId(flowId), principal.GetUserId()), ct);
        return Results.Ok(PublishResponse.From(result));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new { error = flowNotFound });
    }
});

// Trigger + execute synchronously. There is no background job/queue
// infrastructure in this repo yet (that's Volume 1 Part V / Volume 4
// territory) — a real deployment would enqueue the run and let a worker
// call FlowEngine.Execute out of band, returning 202 Accepted immediately.
// This endpoint runs the flow inline and returns the completed result, a
// deliberate, temporary simplification analogous to Yukti.Host's smoke test.
flows.MapPost("/{flowId:guid}/runs", async (
    Guid flowId, TriggerRunRequest? req, ClaimsPrincipal principal,
    IFlowRepository flowRepo, IFlowRunRepository runRepo,
    TriggerFlowRunCommandHandler triggerHandler, FlowEngine engine, ICredentialResolver credentials,
    CancellationToken ct) =>
{
    var flow = await flowRepo.GetById(new FlowId(flowId), ct);
    if (flow is null) return Results.NotFound(new { error = flowNotFound });
    if (flow.Status != FlowStatus.Published)
        return Results.BadRequest(new { error = $"Flow is {flow.Status}; only Published flows can be run." });

    var runId = await triggerHandler.Handle(
        new TriggerFlowRunCommand(flow.Id, RunTrigger.Api, JsonParamNormalizer.Normalize(req?.VariableOverrides), principal.GetTenantId(), principal.GetUserId()), ct);

    var run = (await runRepo.GetById(runId, ct))!;
    var completed = await engine.Execute(flow, run, credentials, ct);

    return Results.Ok(FlowRunResponse.From(completed));
});

var runs = app.MapGroup("/api/runs").WithTags("Runs").RequireAuthorization();

runs.MapGet("/{runId:guid}", async (Guid runId, IFlowRunRepository repo, CancellationToken ct) =>
{
    var run = await repo.GetById(new FlowRunId(runId), ct);
    return run is null ? Results.NotFound(new { error = runNotFound }) : Results.Ok(FlowRunResponse.From(run));
});

runs.MapPost("/{runId:guid}/cancel", async (Guid runId, ClaimsPrincipal principal, CancelFlowRunCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        await handler.Handle(new CancelFlowRunCommand(new FlowRunId(runId), principal.GetUserId()), ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new { error = runNotFound });
    }
});

// FR-ORCH-7: introspection surface a flow-authoring GUI renders from
// directly — zero hardcoded per-module knowledge on the client side.
app.MapGet("/api/modules", async (IModuleRegistry registry, IModuleRegistrationRepository registrations, CancellationToken ct) =>
{
    var responses = new List<ModuleResponse>();
    foreach (var module in registry.All)
    {
        var registration = await registrations.GetByKind(module.Kind, null, ct);
        responses.Add(new ModuleResponse(
            module.Kind.Value,
            registration?.DisplayName ?? module.Kind.Value,
            registration?.Trust.ToString() ?? "Unknown",
            module.ContractVersion,
            module.GetSupportedActions().Select(ActionSchemaResponse.From).ToList()));
    }
    return Results.Ok(responses);
}).WithTags("Modules").RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "Yukti.Api", status = "running" }));

app.Run();

static class ClaimsPrincipalExtensions
{
    public static UserId GetUserId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!));

    public static TenantId GetTenantId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue("tenant")!));
}
