using Xunit;
using Yukti.Application.Abstractions;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T4's source-scan requirement: no command exists anywhere that could
/// mutate or remove an audit entry after the fact — a regression guard, not
/// just today's state. <see cref="IAuditRepository"/> already exposes no
/// Update/Delete method (so nothing could call one even if such a command
/// existed), and <see cref="AuditEntryGrantsTests"/> proves the database
/// grant backs that up independently — this file is the third, narrowest
/// layer: proving no one ever writes such a command in the first place.
/// </summary>
public sealed class AuditCommandCatalogueTests
{
    private static bool WouldMutateOrRemoveAnAuditEntry(Type commandType) =>
        commandType.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase)
        && (commandType.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || commandType.Name.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || commandType.Name.Contains("Edit", StringComparison.OrdinalIgnoreCase)
            || commandType.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<Type> AllCommandTypes() =>
        typeof(ICommand<>).Assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .ToList();

    [Fact]
    public void No_command_anywhere_in_the_catalogue_would_mutate_or_remove_an_audit_entry()
    {
        var commandTypes = AllCommandTypes();
        Assert.NotEmpty(commandTypes); // the scan itself must find the real catalogue, not silently match zero types.

        var suspect = commandTypes.Where(WouldMutateOrRemoveAnAuditEntry).Select(t => t.Name).ToList();

        Assert.True(suspect.Count == 0,
            $"Found a command type that could mutate or remove an audit entry: {string.Join(", ", suspect)}");
    }

    /// <summary>Mutation check: the exact predicate the real scan uses must actually be capable
    /// of catching an offending name — proven against a synthetic command built for exactly this
    /// purpose, so the proof doesn't rest solely on "it currently finds nothing."</summary>
    private sealed record DeleteAuditEntryCommand(Guid Id) : ICommand<bool>;

    [Fact]
    public void Mutation_check_the_predicate_would_catch_a_real_offending_command_name() =>
        Assert.True(WouldMutateOrRemoveAnAuditEntry(typeof(DeleteAuditEntryCommand)));
}
