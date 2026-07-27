using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using Yukti.Api;
using Role = Yukti.Domain.IdentityAccess.Role; // disambiguates from StackExchange.Redis.Role
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Application.ModulePlugin;
using Yukti.Contracts;
using Yukti.Domain.Events;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Infrastructure.ReadModels;
using Yukti.Orchestration;

var builder = WebApplication.CreateBuilder(args);

// FR-OPS-02: graceful shutdown drains in-flight FlowRun execution to the
// next step boundary before the process exits. This already works
// architecturally — flows.MapPost("/runs") passes context.RequestAborted
// into FlowEngine.Execute, whose loop only calls
// ct.ThrowIfCancellationRequested() once per step iteration (never
// mid-step, per its own doc comment on the incremental-commit design) —
// so a SIGTERM mid-step lets that step finish and commit, then stops
// before dispatching the next one. The default 5s host shutdown timeout
// is too short for that to reliably happen before ASP.NET Core aborts
// the connection outright; this gives every in-flight step room to reach
// its natural boundary first.
builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

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

// ---- OpenTelemetry (FR-OBS-01/02) ----
// AddSource/AddMeter("Yukti.Orchestration") is the only thing that turns
// FlowEngine's Activity/Meter calls (OrchestrationTelemetry.cs) into real
// exported traces/metrics — without this registration those calls are
// harmless no-ops (Activity.StartActivity returns null with no listener).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Yukti.Api"))
    .WithTracing(tracing => tracing
        .AddSource(Yukti.Orchestration.OrchestrationTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(Yukti.Orchestration.OrchestrationTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

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
        // FR-RT-01: SignalR's browser transports (WebSocket/SSE) can't set
        // a custom Authorization header, so the JS client sends the token
        // as an ?access_token= query parameter instead — which the JWT
        // bearer handler ignores by default. Found live, testing a real
        // browser client against RunProgressHub for the first time this
        // hub has ever been exercised outside a server-to-server test:
        // every negotiate attempt failed pre-flight, this hub was never
        // actually reachable from a browser before. Scoped to hub paths
        // only — ordinary REST endpoints keep using the header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
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
// Tenant-scoped for authenticated endpoints; IP-scoped for the
// unauthenticated auth endpoints (login/register — brute-force protection
// before any tenant identity exists to key off). Redis-backed via the same
// dedicated "yukti-redis" instance the trigger lock/SignalR backplane use
// (registered as IConnectionMultiplexer later in this file — resolved here
// through context.RequestServices, not a direct reference, since this
// lambda runs per-request after the DI container is fully built, not at
// registration time) — every horizontally-scaled Yukti.Api instance now
// shares the same counters, closing the real gap the previous in-memory
// SlidingWindowRateLimiter left (correct alone, blind to every other
// replica).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PerTenant", context =>
    {
        var tenant = context.User.FindFirst("tenant")?.Value ?? "anonymous";
        var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        return RateLimitPartition.Get(tenant, _ => new RedisFixedWindowRateLimiter(
            redis, $"yukti:ratelimit:tenant:{tenant}", permitLimit: 100, window: TimeSpan.FromSeconds(10)));
    });

    options.AddPolicy("PerIp", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        return RateLimitPartition.Get(ip, _ => new RedisFixedWindowRateLimiter(
            redis, $"yukti:ratelimit:ip:{ip}", permitLimit: 10, window: TimeSpan.FromMinutes(1)));
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
//
// FR-AUDIT-03: the app's RUNTIME connection uses "yukti_app", a distinct,
// non-owner role with only SELECT/INSERT on audit_entries (no
// UPDATE/DELETE) — see the AddYuktiAppRuntimeRole migration. "Yukti"
// (the original, owner-privileged connection as "pranav") is what
// `dotnet ef database update` still uses to run migrations, since DDL /
// RLS policy creation needs owner privileges yukti_app deliberately
// doesn't have. Falls back to ConnectionStrings:Yukti if
// ConnectionStrings:YuktiRuntime isn't configured, so this doesn't break
// any environment that hasn't set up the split role yet.
var connectionString = builder.Configuration.GetConnectionString("YuktiRuntime")
    ?? builder.Configuration.GetConnectionString("Yukti")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:YuktiRuntime/Yukti. Set one via `dotnet user-secrets set \"ConnectionStrings:Yukti\" \"...\"` for local development.");
builder.Services.AddYuktiInfrastructure(connectionString);

// FR-TENANT-01/FR-DB-02 fallout: login/self-registration/startup seeding
// all need to find a user by email before any tenant context exists —
// RLS on the users table has no permissive branch for that, so this
// bypasses it via yukti_worker (falls back to the regular connection
// string in environments that haven't set up the split roles yet).
var bypassConnectionString = builder.Configuration.GetConnectionString("YuktiWorker") ?? connectionString;
builder.Services.AddSingleton<IAuthBypassUserLookup>(_ => new EfAuthBypassUserLookup(bypassConnectionString));

// Domain event dispatch (Tier 1, in-process) is orthogonal to which
// Infrastructure implementation persists state — kept as-is regardless of
// InMemory vs. EF Core.
// FR-SCHED-03/FR-RT-03: dedicated Redis instance for the distributed
// trigger lock and the SignalR backplane — configurable via
// ConnectionStrings:Redis, defaulting to the local "yukti-redis" Docker
// container (port 6380) provisioned specifically for this project — not
// shared with any other project's Redis instance.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6380";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

// FR-RT-01: live progress push. FR-RT-03: the Redis backplane below is
// what makes an event raised on one Yukti.Api instance reach a client
// connected to another — see RunProgressHub's own doc comment.
builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment())
    .AddStackExchangeRedis(redisConnectionString, options =>
        options.Configuration.ChannelPrefix = RedisChannel.Literal("yukti-signalr"));

builder.Services.AddSingleton<InMemoryDomainEventDispatcher>();
builder.Services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<InMemoryDomainEventDispatcher>());

// Credential resolution has no persistence need yet (Vault-backed
// resolution is Volume 1 Part III §18.4/Volume 4's follow-up) — still the
// in-memory stand-in.
builder.Services.AddSingleton<ICredentialResolver, InMemoryCredentialResolver>();

// FR-OPS-01: the Scheduler (FR-SCHED), outbox relay (FR-EVT-01), and trend
// batch job (FR-CQRS-03) now run in the separate Yukti.Worker deployable —
// see its Program.cs — not in this HTTP-serving process. Yukti.Api keeps
// only what an inbound HTTP request needs: the SignalR hub/backplane above
// (FR-RT stays here — it's push-to-connected-client, not a background job)
// and the trend read endpoint below (a query against TrendAggregateReadModel,
// the table Yukti.Worker's batch job writes into).

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
// Scoped, not Singleton: ModuleDispatcher now depends on the Scoped EF
// IModuleRegistrationRepository (FR-PLUGIN-04's trust-tier lookup) — same
// captive-dependency reasoning as FlowEngine below.
builder.Services.AddScoped<IModuleDispatcher, ModuleDispatcher>();
builder.Services.AddSingleton<IModuleExecutionStrategySelector, ModuleExecutionStrategySelector>();
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

// FR-RT-01: wires Tier 1 domain events straight to SignalR groups —
// see RunProgressBridge's own doc comment for why this is Tier 1 only.
RunProgressBridge.Wire(app.Services.GetRequiredService<InMemoryDomainEventDispatcher>(),
    app.Services.GetRequiredService<IHubContext<RunProgressHub>>());

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
        await ProblemResults.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden", ex.Message, context.RequestAborted);
    }
    catch (DomainException ex)
    {
        await ProblemResults.WriteAsync(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message, context.RequestAborted);
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

    var authBypass = scope.ServiceProvider.GetRequiredService<IAuthBypassUserLookup>();
    var bootstrapAdmin = await authBypass.GetByEmail("admin@yukti.local", default);
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
    LoginRequest req, HttpContext context, IAuthBypassUserLookup authBypass, IRoleRepository roleRepo,
    IPasswordHasher hasher, IJwtTokenService jwt, IRefreshTokenStore refreshTokens, CancellationToken ct) =>
{
    var user = await authBypass.GetByEmail(req.Email, ct);
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
    RefreshRequest req, HttpContext context, IRefreshTokenStore refreshTokens, IAuthBypassUserLookup authBypass, IRoleRepository roleRepo,
    IJwtTokenService jwt, CancellationToken ct) =>
{
    var userId = await refreshTokens.Consume(req.RefreshToken, ct);
    if (userId is null)
        return ProblemResults.Unauthorized(context, "Refresh token is invalid, expired, or already used.");

    // Found live via the frontend build: this endpoint is anonymous (no
    // JWT yet, that's the whole point of refreshing one) — the ordinary
    // RLS-enforced IUserRepository.GetById filters by an ambient tenant
    // that never exists here, so it always returned null and refresh
    // always 401'd, on every account, regardless of token validity. Same
    // bypass IAuthBypassUserLookup.GetByEmail already uses for login.
    var user = await authBypass.GetById(userId.Value, ct);
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

// FR-CQRS-01: reads the same flows table any write above just landed in —
// no separate synced copy, so "read your own writes" holds with zero lag
// by construction, not by a freshness guarantee bolted on afterward.
flows.MapGet("/", async (ClaimsPrincipal principal, IFlowSummaryQuery query, CancellationToken ct) =>
    Results.Ok(await query.ListByTenant(principal.GetTenantId(), ct)));

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

// FR-CQRS-03: staleness (LastUpdatedAt) is part of the payload, not
// something callers have to separately infer.
app.MapGet($"{apiV1}/trends", async (ClaimsPrincipal principal, YuktiDbContext db, CancellationToken ct) =>
{
    var trend = await db.Set<Yukti.Infrastructure.ReadModels.TrendAggregateReadModel>()
        .FirstOrDefaultAsync(t => t.TenantId == principal.GetTenantId(), ct);
    return trend is null ? Results.NoContent() : Results.Ok(trend);
}).WithTags("Trends").RequireAuthorization().RequireRateLimiting("PerTenant");

// FR-RT-01/02: clients call JoinRun(flowRunId) after connecting, then
// GET /api/v1/runs/{runId} once for full current state (the catch-up
// fetch FR-RT-02 requires on every reconnect) before trusting any
// subsequently pushed event.
app.MapHub<RunProgressHub>("/hubs/run-progress").RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "Yukti.Api", status = "running" }));

app.Run();

static class ClaimsPrincipalExtensions
{
    public static UserId GetUserId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!));

    public static TenantId GetTenantId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue("tenant")!));
}
