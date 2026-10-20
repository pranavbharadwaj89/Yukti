using Microsoft.Extensions.Configuration;
using Xunit;
using Yukti.Infrastructure.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// DISC-14: the real, file-based <see cref="FileAiRequestQuotaLedger"/> — exercised directly
/// (not through a fake), since the entire point of this class is a shared on-disk format two
/// different runtimes (.NET and Sarvaghna's Node <c>gemini-client.mjs</c>) both read and write.
/// The camelCase test below is a regression test for a real bug caught manually while verifying
/// D5-T8's real eval harness: .NET's <c>JsonSerializer</c> defaults to PascalCase, which silently
/// deserializes a camelCase line (Node's natural JSON shape) to all-default field values — no
/// exception, just a count of zero when the real answer was 20.
/// </summary>
public sealed class FileAiRequestQuotaLedgerTests : IDisposable
{
    private readonly string _ledgerPath = Path.Combine(Path.GetTempPath(), $"yukti-quota-ledger-test-{Guid.NewGuid():N}.jsonl");

    public void Dispose() => File.Delete(_ledgerPath);

    private FileAiRequestQuotaLedger BuildLedger()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Budget:RequestQuotaLedgerPath"] = _ledgerPath })
            .Build();
        return new FileAiRequestQuotaLedger(config);
    }

    [Fact]
    public async Task A_record_written_by_this_class_is_counted_by_this_class()
    {
        var ledger = BuildLedger();

        await ledger.RecordAsync("sarvagna-dotnet", "coder", CancellationToken.None);
        await ledger.RecordAsync("sarvagna-dotnet", "analyst", CancellationToken.None);

        Assert.Equal(2, await ledger.CountTodayAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_hand_written_camelCase_line_matching_nodes_own_shape_is_counted_correctly()
    {
        // Exactly the line shape sarvaghna/core/gemini-client.mjs's recordRequest() writes — proves
        // the two runtimes can actually read each other's ledger entries, not just their own.
        var nowUtc = DateTimeOffset.UtcNow.ToString("O");
        await File.WriteAllTextAsync(_ledgerPath,
            $$"""{"timestampUtc":"{{nowUtc}}","source":"sarvaghna-node","capability":"semantic-review"}""" + "\n");

        var ledger = BuildLedger();

        Assert.Equal(1, await ledger.CountTodayAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Entries_from_a_previous_day_are_not_counted_toward_today()
    {
        var yesterdayUtc = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        await File.WriteAllTextAsync(_ledgerPath,
            $$"""{"timestampUtc":"{{yesterdayUtc}}","source":"sarvagna-dotnet","capability":"coder"}""" + "\n");

        var ledger = BuildLedger();

        Assert.Equal(0, await ledger.CountTodayAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_line_is_skipped_not_fatal_undercounting_is_the_safe_direction()
    {
        var nowUtc = DateTimeOffset.UtcNow.ToString("O");
        await File.WriteAllTextAsync(_ledgerPath,
            "{not valid json at all\n" +
            $$"""{"timestampUtc":"{{nowUtc}}","source":"sarvagna-dotnet","capability":"coder"}""" + "\n");

        var ledger = BuildLedger();

        Assert.Equal(1, await ledger.CountTodayAsync(CancellationToken.None)); // the one good line still counts
    }

    [Fact]
    public async Task No_ledger_file_yet_counts_as_zero_not_an_error()
    {
        var ledger = BuildLedger(); // _ledgerPath deliberately never created

        Assert.Equal(0, await ledger.CountTodayAsync(CancellationToken.None));
    }
}
