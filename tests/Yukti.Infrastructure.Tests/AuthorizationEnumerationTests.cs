using System.Reflection;
using Xunit;
using Yukti.Application.Auditing;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T2's own upgrade past AuthorizationTests.cs's hand-enumerated 28
/// cases, which the Day 6 spec names explicitly as "the weak form" — it
/// protects today's commands but a new one added later with no permission
/// check passes silently, because nothing scans for it. This test
/// discovers every write-command handler via reflection over the
/// AuditableCommandHandler&lt;,&gt; base type (FR-AUDIT-01: every command
/// handler inherits it, enforced at compile time by Yukti.Analyzers'
/// YUKTI002), then scans each one's own HandleCore method body for a call
/// to IPermissionChecker.EnsurePermission — a handler with none fails this
/// test by its own type name, not by a human remembering to add a case.
///
/// This checks "the call exists somewhere in HandleCore," not literally
/// "is the first statement" (FR-AUTHZ-02's stronger wording) — proving the
/// latter needs a full IL control-flow read, not just a presence scan.
/// What this catches is the failure mode the spec actually cares about
/// most: a handler with no permission check at all, invisible to review.
/// AuthorizationTests.cs's 28 hand-built success/403 pairs are kept as-is
/// underneath this, proving the check that exists actually works per role —
/// this file proves one exists at all, for every handler, forever.
/// </summary>
public sealed class AuthorizationEnumerationTests
{
    /// <summary>Open self-registration by design — no caller identity exists yet
    /// to check a permission against (same exclusion AuthorizationTests.cs
    /// already documents and applies).</summary>
    private static readonly HashSet<string> ExemptHandlers = new() { "RegisterUserCommandHandler" };

    private static IEnumerable<Type> DiscoverCommandHandlerTypes()
    {
        var assembly = typeof(AuditableCommandHandler<,>).Assembly;
        return assembly.GetTypes().Where(IsConcreteAuditableCommandHandler);
    }

    private static bool IsConcreteAuditableCommandHandler(Type type)
    {
        if (type.IsAbstract) return false;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AuditableCommandHandler<,>))
                return true;
        }
        return false;
    }

    private static bool CallsEnsurePermission(Type handlerType)
    {
        var handleCore = handlerType.GetMethod("HandleCore",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(handleCore); // every AuditableCommandHandler<,> subtype must override this — a missing override is a compile error, not a runtime gap.

        // HandleCore is always `async` here — its own IL is just the compiler's
        // state-machine bootstrap (AsyncTaskMethodBuilder.Start), never the real
        // body. The actual call this test is looking for lives in the
        // compiler-generated state machine's MoveNext, named via
        // [AsyncStateMachine] on the method that kicks it off.
        var stateMachineAttr = handleCore!.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>();
        var scanTarget = stateMachineAttr is not null
            ? stateMachineAttr.StateMachineType!.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : handleCore;
        Assert.NotNull(scanTarget);

        var body = scanTarget!.GetMethodBody();
        if (body is null) return false;
        var il = body.GetILAsByteArray()!;
        var module = scanTarget.Module;

        for (var i = 0; i + 4 < il.Length; i++)
        {
            // call = 0x28, callvirt = 0x6F — both take a 4-byte little-endian
            // metadata token operand immediately after the opcode byte.
            if (il[i] != 0x28 && il[i] != 0x6F) continue;

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var resolved = module.ResolveMethod(token);
                if (resolved is not null
                    && resolved.Name == nameof(Yukti.Application.IdentityAccess.IPermissionChecker.EnsurePermission)
                    && resolved.DeclaringType == typeof(Yukti.Application.IdentityAccess.IPermissionChecker))
                {
                    return true;
                }
            }
            catch
            {
                // Not a real token at this byte offset — expected for the vast
                // majority of positions scanned; only a genuine call/callvirt
                // instruction's operand resolves to anything at all.
            }
        }

        return false;
    }

    [Fact]
    public void Every_write_command_handler_calls_EnsurePermission_somewhere_in_HandleCore()
    {
        var handlers = DiscoverCommandHandlerTypes()
            .Where(t => !ExemptHandlers.Contains(t.Name))
            .ToList();

        Assert.NotEmpty(handlers); // the discovery mechanism itself must find something real, not silently match zero types.

        var missing = handlers.Where(h => !CallsEnsurePermission(h)).Select(h => h.Name).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0,
            $"The following command handler(s) have no call to IPermissionChecker.EnsurePermission anywhere in HandleCore: {string.Join(", ", missing)}");
    }

    // --- Mutation check, recorded as a standing test rather than a one-off manual exercise ---
    // (also exercised by hand against a real handler: AssignRoleCommandHandler's own
    // EnsurePermission line was temporarily commented out, the enumeration test above failed
    // naming exactly "AssignRoleCommandHandler", and the line was restored — the same evidence
    // the spec asks for, just not left as permanently-mutated code in the production handler.)

    private sealed record SyntheticCommand(Yukti.Domain.SharedKernel.UserId ActorId) : Yukti.Application.Abstractions.ICommand<bool>;

    private sealed class HandlerWithCheck : AuditableCommandHandler<SyntheticCommand, bool>
    {
        private readonly Yukti.Application.IdentityAccess.IPermissionChecker _permissions;
        public HandlerWithCheck(Yukti.Application.IdentityAccess.IPermissionChecker permissions, Yukti.Application.Abstractions.IAuditRepository audit,
            Yukti.Application.Abstractions.ITenantContextAccessor tenantAccessor, Yukti.Application.Abstractions.IUnitOfWorkFactory uowFactory)
            : base(audit, tenantAccessor, uowFactory) => _permissions = permissions;

        protected override async Task<bool> HandleCore(SyntheticCommand cmd, CancellationToken ct)
        {
            await _permissions.EnsurePermission(cmd.ActorId, Yukti.Domain.IdentityAccess.Permission.UserManage, ct);
            return true;
        }
    }

    private sealed class HandlerWithoutCheck : AuditableCommandHandler<SyntheticCommand, bool>
    {
        public HandlerWithoutCheck(Yukti.Application.Abstractions.IAuditRepository audit,
            Yukti.Application.Abstractions.ITenantContextAccessor tenantAccessor, Yukti.Application.Abstractions.IUnitOfWorkFactory uowFactory)
            : base(audit, tenantAccessor, uowFactory) { }

        protected override Task<bool> HandleCore(SyntheticCommand cmd, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public void Mutation_check_the_detector_tells_a_real_check_apart_from_a_missing_one()
    {
        Assert.True(CallsEnsurePermission(typeof(HandlerWithCheck)));
        Assert.False(CallsEnsurePermission(typeof(HandlerWithoutCheck)));
    }

    [Fact]
    public void RegisterUserCommandHandler_is_the_only_exempt_handler_and_it_is_exempt_by_design_not_oversight()
    {
        var registerHandler = DiscoverCommandHandlerTypes().Single(t => t.Name == "RegisterUserCommandHandler");
        Assert.False(CallsEnsurePermission(registerHandler));
        Assert.Single(ExemptHandlers);
    }
}
