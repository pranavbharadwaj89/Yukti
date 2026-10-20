using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// DISC-14: a plain, append-only JSON-lines file at <c>sarvaghna/ledger/gemini-requests.jsonl</c> —
/// the same <c>sarvaghna/ledger/</c> directory already used (and already gitignored) for
/// Sarvaghna's own decision log. File-based, not Postgres, specifically because this quota is
/// per API *key*, shared across two separate runtimes that don't otherwise share any storage:
/// Sarvagna's own .NET calls (API, CLI eval harness) and Sarvaghna's Node commit/push-hook AI
/// reviews (<c>sarvaghna/core/gemini-client.mjs</c>, which writes the identical line shape to the
/// identical file). Both sides read the same file to decide "how many real requests today."
///
/// Concurrency: a <see cref="SemaphoreSlim"/> serializes writes within *this* process only —
/// true cross-process file locking is deliberately not built here. Call volume in practice is a
/// handful of requests a day from a single developer's machine/CI run, never truly concurrent;
/// a lost line in a rare race only ever undercounts, the same safe-direction tradeoff
/// <see cref="AiPricing.Zero"/> already accepts for an unpriced provider.
/// </summary>
public sealed class FileAiRequestQuotaLedger : IAiRequestQuotaLedger
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    // The Node side (sarvaghna/core/gemini-client.mjs) writes/reads plain camelCase JSON, the
    // natural shape for JS — this must match exactly, or the two runtimes silently can't read
    // each other's lines (confirmed the hard way: .NET's JsonSerializer default is PascalCase,
    // which deserializes a camelCase line's properties to all-default values with no exception,
    // undercounting to zero instead of failing loudly).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _ledgerPath;

    public FileAiRequestQuotaLedger(IConfiguration config)
    {
        _ledgerPath = config["Ai:Budget:RequestQuotaLedgerPath"]
            ?? Path.Combine(RepoRoot(), "sarvaghna", "ledger", "gemini-requests.jsonl");
    }

    public async Task<int> CountTodayAsync(CancellationToken ct)
    {
        if (!File.Exists(_ledgerPath)) return 0;

        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
        var lines = await File.ReadAllLinesAsync(_ledgerPath, ct);
        var count = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<LedgerEntry>(line, JsonOptions);
                if (entry is not null && entry.TimestampUtc.UtcDateTime.Date == today) count++;
            }
            catch (JsonException)
            {
                // A malformed line (e.g. truncated by a crash mid-write) is skipped, not fatal —
                // undercounting here is the safe direction, same reasoning as the class doc comment.
            }
        }
        return count;
    }

    public async Task RecordAsync(string source, string capability, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_ledgerPath)!;
        Directory.CreateDirectory(dir);
        var line = JsonSerializer.Serialize(new LedgerEntry(DateTimeOffset.UtcNow, source, capability), JsonOptions);

        await WriteLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_ledgerPath, line + Environment.NewLine, ct);
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln) from " + AppContext.BaseDirectory);
    }

    private sealed record LedgerEntry(DateTimeOffset TimestampUtc, string Source, string Capability);
}
