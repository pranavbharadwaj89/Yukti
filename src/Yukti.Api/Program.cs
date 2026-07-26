using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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
using Yukti.Infrastructure;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

var builder = WebApplication.CreateBuilder(args);

// ---- Structured logging (FR-LOG) ----
// ASP.NET Core already wires up Microsoft.Extensions.Logging with a console
// provider by default — every ILogger<T> constructor dependency below
// (FlowEngine, RetryFlakeHandler, ModuleDispatcher) resolves through that
// with zero extra registration. This just swaps the default text formatter
// for JSON (structured sinks expect one event per line, not columns) and
// turns scopes on so FlowEngine.Execute's BeginScope(FlowRunId) (FR-LOG-03)
// actually appears in output instead of being silently dropped.
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});

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

// ---- CORS ----
// No browser frontend has existed against this API until now, so no
// policy existed either — a real gap, since without one no browser-based
// client on a different origin can call any endpoint at all. Allowed
// origins are configuration-driven (Cors:AllowedOrigins), defaulting to
// the two most common local dev server ports; a real deployment sets this
// via environment/config to the actual frontend origin(s), never "*".
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("YuktiCors", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ---- Rate limiting (FR-API-04) ----
// Tenant-scoped sliding window for authenticated endpoints; IP-scoped for
// the unauthenticated auth endpoints (login/register — brute-force
// protection before any tenant identity exists to key off). In-memory,
// single-process only, via .NET's built-in RateLimiter middleware — the FR
// calls for a Redis-backed mechanism shared across horizontally-scaled
// instances, which needs an actual Redis deployment this environment
// doesn't have. Structured so swapping to a Redis-backed partitioned
// limiter later is a store change, not a redesign: the partition key
// (tenant id / remote IP) and policy shape stay the same.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PerTenant", context =>
    {
        var tenant = context.User.FindFirst("tenant")?.Value ?? "anonymous";
        return RateLimitPartition.GetSlidingWindowLimiter(tenant, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 5,
            QueueLimit = 0,
        });
    });

    options.AddPolicy("PerIp", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });
});

// ---- Composition root ----
// Real, durable persistence now: AddYuktiInfrastructure registers EF Core
// repositories + IUnitOfWorkFactory against CockroachDB, replacing every
// InMemory repository/UoW registration this file previously had — no other
// file changed to make this swap (exactly the plan the README and
// INIT-YUKTI-BACKEND-001's Assumption A-02 both call for). The connection
// string is read from configuration (user-secrets in Development, an env
// var / real secret store in any other environment) — never hardcoded,
// never committed.
var connectionString = builder.Configuration.GetConnectionString("Yukti")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:Yukti. Set it via `dotnet user-secrets set \"ConnectionStrings:Yukti\" \"...\"` for local development.");
builder.Services.AddYuktiInfrastructure(connectionString);

// Domain event dispatch (Tier 1, in-process) is orthogonal to which
// Infrastructure implementation persists state — kept as-is regardless of
// InMemory vs. EF Core.
builder.Services.AddSingleton<InMemoryDomainEventDispatcher>();
builder.Services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<InMemoryDomainEventDispatcher>());

// Credential resolution has no persistence need yet (Vault-backed
// resolution is Volume 1 Part III §18.4/Volume 4's follow-up) — still the
// in-memory stand-in.
builder.Services.AddSingleton<ICredentialResolver, InMemoryCredentialResolver>();

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService>(_ => new JwtTokenService(signingKey));
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();

// FR-TENANT-01/02: tenant context sourced only from the authenticated
// principal's JWT claim (HttpContextTenantAccessor), consumed by every
// tenant-scoped repository query (Layer 1) and by TenantGuard (Layer 3) —
// see Repositories.cs and TenantContext.cs for the other two layers.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextAccessor, HttpContextTenantAccessor>();
builder.Services.AddScoped<ITenantGuard, TenantGuard>();

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
// Scoped, not Singleton: FlowEngine now depends on Scoped EF repositories
// and IUnitOfWorkFactory (a DbContext is not thread-safe / must not
// outlive one request) — a Singleton FlowEngine holding a Scoped
// dependency would be a captive-dependency DI error.
builder.Services.AddScoped<FlowEngine>();

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

// ---- Global exception -> RFC 7807 mapping (FR-API-03) ----
// Keeps every endpoint below free of repeated try/catch for the two
// cross-cutting failure modes every authorized command can now raise:
// ForbiddenException (FR-AUTHZ-02's EnsurePermission) and DomainException
// (any aggregate invariant). Endpoint-local catches still handle
// "not found" (InvalidOperationException), which needs a different message
// per resource type. Every response — this middleware's and every
// endpoint's — is a proper application/problem+json body with a
// correlationId (ProblemResults.cs).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ForbiddenException ex)
    {
        await ProblemResults.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden", ex.Message);
    }
    catch (DomainException ex)
    {
        await ProblemResults.WriteAsync(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message);
    }
});

app.UseCors("YuktiCors");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// FR-TENANT-01 Layer 2: sets the session-level Postgres/CockroachDB
// setting the RLS policies (see the RLS migration) key off, once per
// request, right after the JWT claims are available and before any
// endpoint touches the database. Resolves the same Scoped YuktiDbContext
// instance the repositories for this request will use, so the SET stays
// in effect for every query in the request — RLS is real database-level
// enforcement, independent of both the repository filter (Layer 1) and
// TenantGuard (Layer 3): even a bug in either of those still can't return
// another tenant's rows once RLS is enabled on the table.
app.Use(async (context, next) =>
{
    var tenantAccessor = context.RequestServices.GetRequiredService<ITenantContextAccessor>();
    if (tenantAccessor.CurrentTenantId is { } tenantId)
    {
        var db = context.RequestServices.GetRequiredService<YuktiDbContext>();
        await db.Database.OpenConnectionAsync(context.RequestAborted);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId.Value.ToString()}, false)", context.RequestAborted);
    }
    await next();
});

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
//
// Idempotent — this now runs against a durable database, so every process
// restart must find the same rows rather than re-inserting (duplicate
// emails/kinds would violate the unique constraints and crash startup on
// the second run).
RoleId adminRoleId, authorRoleId, runnerRoleId;
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
    var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var uowFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

    var admin = await roles.GetByName("Administrator", null, default) ?? Role.CreateBaselineAdministrator();
    var author = await roles.GetByName("Flow Author", null, default) ?? Role.CreateBaselineFlowAuthor();
    var runner = await roles.GetByName("Flow Runner", null, default) ?? Role.CreateBaselineFlowRunner();
    await roles.Save(admin, default);
    await roles.Save(author, default);
    await roles.Save(runner, default);
    adminRoleId = admin.Id;
    authorRoleId = author.Id;
    runnerRoleId = runner.Id;

    var bootstrapAdmin = await users.GetByEmail("admin@yukti.local", default);
    if (bootstrapAdmin is null)
    {
        bootstrapAdmin = Yukti.Domain.IdentityAccess.User.Register(
            "admin@yukti.local", "Bootstrap Administrator", TenantId.New(), hasher.Hash("ChangeMe123!"));
        bootstrapAdmin.AssignRole(adminRoleId);
        await users.Save(bootstrapAdmin, default);

        // The users table's RLS policy (Layer 2) checks writes as well as
        // reads (FORCE ROW LEVEL SECURITY applies to every command) — with
        // no authenticated request here, app.current_tenant_id is unset,
        // so without this the INSERT below would violate the policy's
        // WITH CHECK. Setting it to the tenant actually being created is
        // the correct fix, not a bypass: every write, even at startup,
        // establishes real tenant context rather than skipping RLS for it.
        var seedDb = scope.ServiceProvider.GetRequiredService<YuktiDbContext>();
        await seedDb.Database.OpenConnectionAsync();
        await seedDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {bootstrapAdmin.TenantId.Value.ToString()}, false)");
    }

    await using var uow = uowFactory.Create();
    await uow.Commit(default);

    var moduleRegistrations = scope.ServiceProvider.GetRequiredService<IModuleRegistrationRepository>();
    var registerHandler = scope.ServiceProvider.GetRequiredService<RegisterModuleCommandHandler>();
    var apiModule = scope.ServiceProvider.GetRequiredService<ApiModule>();
    var logsModule = scope.ServiceProvider.GetRequiredService<LogsModule>();

    if (await moduleRegistrations.GetByKind(ModuleKind.Api, null, default) is null)
        await registerHandler.Handle(new RegisterModuleCommand(
            ModuleKind.Api, "API Automation", TrustTier.BuiltIn, apiModule.GetSupportedActions(), apiModule.ContractVersion, bootstrapAdmin.Id), default);
    if (await moduleRegistrations.GetByKind(ModuleKind.Logs, null, default) is null)
        await registerHandler.Handle(new RegisterModuleCommand(
            ModuleKind.Logs, "Log Automation", TrustTier.BuiltIn, logsModule.GetSupportedActions(), logsModule.ContractVersion, bootstrapAdmin.Id), default);
}

const string flowNotFound = "Flow not found.";
const string runNotFound = "FlowRun not found.";
const string invalidCredentials = "Invalid email or password."; // FR-AUTH-05: identical for "no such user" and "wrong password"

// ---- API versioning (FR-API-05) ----
// Path-segment versioning: every resource endpoint sits under /api/v1.
// Only v1 exists today, so there is nothing to dual-serve yet — this
// establishes the scheme a future /api/v2 needs to coexist with v1 for a
// 90-day deprecation window, per the FR. "/" stays unversioned: it's a
// liveness/infra endpoint, not a versioned business resource.
const string apiV1 = "/api/v1";

// ---- Auth endpoints ----
var auth = app.MapGroup($"{apiV1}/auth").WithTags("Auth").RequireRateLimiting("PerIp");

// Open self-registration, defaulting to the Flow Author baseline role in a
// brand-new tenant (pooled multi-tenancy's signup path — FR-TENANT-03).
// Real deployments may want to gate this differently (invite-only, SSO
// provisioning); left open here since there is no other way to reach a
// second user without an existing Administrator.
auth.MapPost("/register", async (RegisterRequest req, RegisterUserCommandHandler handler, CancellationToken ct) =>
{
    var userId = await handler.Handle(
        new RegisterUserCommand(req.Email, req.Password, req.DisplayName, TenantId.New(), authorRoleId), ct);
    return Results.Created($"{apiV1}/auth/users/{userId.Value}", new { userId = userId.Value });
});

auth.MapPost("/login", async (
    LoginRequest req, HttpContext context, IUserRepository users, IRoleRepository roleRepo,
    IPasswordHasher hasher, IJwtTokenService jwt, IRefreshTokenStore refreshTokens, CancellationToken ct) =>
{
    var user = await users.GetByEmail(req.Email, ct);
    if (user is null || !hasher.Verify(req.Password, user.PasswordHash))
        return ProblemResults.Unauthorized(context, invalidCredentials);

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
    RefreshRequest req, HttpContext context, IRefreshTokenStore refreshTokens, IUserRepository users, IRoleRepository roleRepo,
    IJwtTokenService jwt, CancellationToken ct) =>
{
    var userId = await refreshTokens.Consume(req.RefreshToken, ct);
    if (userId is null)
        return ProblemResults.Unauthorized(context, "Refresh token is invalid, expired, or already used.");

    var user = await users.GetById(userId.Value, ct);
    if (user is null)
        return ProblemResults.Unauthorized(context, invalidCredentials);

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
app.MapPost($"{apiV1}/roles/{{roleId:guid}}/permissions", async (
    Guid roleId, UpdateRolePermissionsRequest req, ClaimsPrincipal principal,
    UpdateRolePermissionsCommandHandler handler, CancellationToken ct) =>
{
    var permissions = req.Permissions.Select(p => Enum.Parse<Permission>(p, ignoreCase: true)).ToHashSet();
    var newVersion = await handler.Handle(
        new UpdateRolePermissionsCommand(new RoleId(roleId), permissions, principal.GetUserId()), ct);
    return Results.Ok(new { roleId, version = newVersion });
}).RequireAuthorization().RequireRateLimiting("PerTenant").WithTags("Auth");

// Was a real gap: AssignRoleCommand existed in Application with no way to
// reach it over HTTP — an Administrator had no way to grant another user a
// role at all via the API.
app.MapPost($"{apiV1}/users/{{userId:guid}}/roles/{{roleId:guid}}", async (
    Guid userId, Guid roleId, ClaimsPrincipal principal, AssignRoleCommandHandler handler, CancellationToken ct) =>
{
    await handler.Handle(new AssignRoleCommand(new UserId(userId), new RoleId(roleId), principal.GetUserId()), ct);
    return Results.NoContent();
}).RequireAuthorization().RequireRateLimiting("PerTenant").WithTags("Auth");

// ---- Flow / Run / Module endpoints — all require authentication now ----
var flows = app.MapGroup($"{apiV1}/flows").WithTags("Flows").RequireAuthorization().RequireRateLimiting("PerTenant");

flows.MapPost("/", async (CreateFlowRequest req, ClaimsPrincipal principal, CreateFlowCommandHandler handler, CancellationToken ct) =>
{
    var flowId = await handler.Handle(new CreateFlowCommand(req.Name, req.Description, principal.GetTenantId(), principal.GetUserId()), ct);
    return Results.Created($"{apiV1}/flows/{flowId.Value}", new { flowId = flowId.Value });
});

flows.MapGet("/{flowId:guid}", async (Guid flowId, HttpContext context, IFlowRepository repo, ITenantGuard tenantGuard, CancellationToken ct) =>
{
    var flow = await repo.GetById(new FlowId(flowId), ct);
    if (flow is null) return ProblemResults.NotFound(context, flowNotFound);
    tenantGuard.EnsureAccessible(flow.TenantId); // FR-TENANT-01 Layer 3
    return Results.Ok(FlowResponse.From(flow));
});

flows.MapPost("/{flowId:guid}/steps", async (Guid flowId, AddStepRequest req, HttpContext context, ClaimsPrincipal principal, AddFlowStepCommandHandler handler, CancellationToken ct) =>
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
        return ProblemResults.NotFound(context, flowNotFound);
    }
});

flows.MapPost("/{flowId:guid}/publish", async (Guid flowId, HttpContext context, ClaimsPrincipal principal, PublishFlowCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        var result = await handler.Handle(new PublishFlowCommand(new FlowId(flowId), principal.GetUserId()), ct);
        return Results.Ok(PublishResponse.From(result));
    }
    catch (InvalidOperationException)
    {
        return ProblemResults.NotFound(context, flowNotFound);
    }
});

// Trigger + execute synchronously. There is no background job/queue
// infrastructure in this repo yet (that's Volume 1 Part V / Volume 4
// territory) — a real deployment would enqueue the run and let a worker
// call FlowEngine.Execute out of band, returning 202 Accepted immediately.
// This endpoint runs the flow inline and returns the completed result, a
// deliberate, temporary simplification analogous to Yukti.Host's smoke test.
//
// FR-API-02: honors an Idempotency-Key header. A retried request with the
// same key (same tenant) returns the original run's result rather than
// triggering a second execution — see IIdempotencyStore.
flows.MapPost("/{flowId:guid}/runs", async (
    Guid flowId, TriggerRunRequest? req, HttpContext context, ClaimsPrincipal principal,
    IFlowRepository flowRepo, IFlowRunRepository runRepo, ITenantGuard tenantGuard,
    TriggerFlowRunCommandHandler triggerHandler, FlowEngine engine, ICredentialResolver credentials,
    IIdempotencyStore idempotency,
    CancellationToken ct) =>
{
    var tenantId = principal.GetTenantId();
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();

    if (idempotencyKey is not null)
    {
        var existingRunId = await idempotency.TryGetResult(tenantId, idempotencyKey, ct);
        if (existingRunId is { } previousRunId)
        {
            var previousRun = await runRepo.GetById(previousRunId, ct);
            if (previousRun is not null)
                return Results.Ok(FlowRunResponse.From(previousRun));
        }
    }

    var flow = await flowRepo.GetById(new FlowId(flowId), ct);
    if (flow is null) return ProblemResults.NotFound(context, flowNotFound);
    tenantGuard.EnsureAccessible(flow.TenantId); // FR-TENANT-01 Layer 3
    if (flow.Status != FlowStatus.Published)
        return ProblemResults.BadRequest(context, $"Flow is {flow.Status}; only Published flows can be run.");

    var runId = await triggerHandler.Handle(
        new TriggerFlowRunCommand(flow.Id, RunTrigger.Api, JsonParamNormalizer.Normalize(req?.VariableOverrides), tenantId, principal.GetUserId()), ct);

    var run = (await runRepo.GetById(runId, ct))!;
    var completed = await engine.Execute(flow, run, credentials, ct);

    if (idempotencyKey is not null)
        await idempotency.Record(tenantId, idempotencyKey, runId, ct);

    return Results.Ok(FlowRunResponse.From(completed));
});

var runs = app.MapGroup($"{apiV1}/runs").WithTags("Runs").RequireAuthorization().RequireRateLimiting("PerTenant");

runs.MapGet("/{runId:guid}", async (Guid runId, HttpContext context, IFlowRunRepository repo, ITenantGuard tenantGuard, CancellationToken ct) =>
{
    var run = await repo.GetById(new FlowRunId(runId), ct);
    if (run is null) return ProblemResults.NotFound(context, runNotFound);
    tenantGuard.EnsureAccessible(run.TenantId); // FR-TENANT-01 Layer 3
    return Results.Ok(FlowRunResponse.From(run));
});

runs.MapPost("/{runId:guid}/cancel", async (Guid runId, HttpContext context, ClaimsPrincipal principal, CancelFlowRunCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        await handler.Handle(new CancelFlowRunCommand(new FlowRunId(runId), principal.GetUserId()), ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException)
    {
        return ProblemResults.NotFound(context, runNotFound);
    }
});

// FR-ORCH-7: introspection surface a flow-authoring GUI renders from
// directly — zero hardcoded per-module knowledge on the client side.
app.MapGet($"{apiV1}/modules", async (IModuleRegistry registry, IModuleRegistrationRepository registrations, CancellationToken ct) =>
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
}).WithTags("Modules").RequireAuthorization().RequireRateLimiting("PerTenant");

app.MapGet("/", () => Results.Ok(new { service = "Yukti.Api", status = "running" }));

app.Run();

static class ClaimsPrincipalExtensions
{
    public static UserId GetUserId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!));

    public static TenantId GetTenantId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue("tenant")!));
}
