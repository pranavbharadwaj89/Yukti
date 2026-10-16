using Microsoft.Extensions.Logging;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T3's stand-in for D5-T9 (not yet built): records are logged, not
/// persisted to <c>ai_cost_records</c> yet. Logging rather than silently
/// dropping — the planning pipeline's "record cost" stage is real today,
/// only its storage is the thing still missing. D5-T9 replaces this with a
/// real implementation of the same <see cref="IAiCostRecorder"/> interface;
/// nothing above this seam changes.
/// </summary>
public sealed class LoggingAiCostRecorder : IAiCostRecorder
{
    private readonly ILogger<LoggingAiCostRecorder> _logger;

    public LoggingAiCostRecorder(ILogger<LoggingAiCostRecorder> logger) => _logger = logger;

    public Task RecordAsync(AiCostRecordData record, CancellationToken ct)
    {
        _logger.LogInformation(
            "AI cost record (D5-T9 persistence not yet built): capability={Capability} provider={Provider} promptVersion={PromptVersion} attempt={Attempt} succeeded={Succeeded} inputTokens={InputTokens} outputTokens={OutputTokens} latencyMs={LatencyMs}",
            record.Capability, record.Provider, record.PromptVersion, record.Attempt, record.Succeeded,
            record.InputTokens, record.OutputTokens, record.Latency.TotalMilliseconds);
        return Task.CompletedTask;
    }
}
