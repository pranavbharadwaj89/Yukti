using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Yukti.Contracts;
using Yukti.Domain.Execution;

namespace Yukti.Orchestration;

public sealed class RetryFlakeHandler : IRetryFlakeHandler
{
    private readonly ILogger<RetryFlakeHandler> _logger;

    public RetryFlakeHandler(ILogger<RetryFlakeHandler> logger) => _logger = logger;

    public async Task<RetryOutcome> ExecuteWithRetry(
        Func<CancellationToken, Task<StepOutcome>> action, RetryPolicy policy, CancellationToken ct)
    {
        var attempts = new List<RetryAttempt>();
        var backoff = policy.InitialBackoff;

        for (var attemptNumber = 1; attemptNumber <= policy.MaxAttempts; attemptNumber++)
        {
            var sw = Stopwatch.StartNew();
            StepOutcome outcome;
            try
            {
                outcome = await action(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = StepOutcome.Failed(ex.Message);
            }
            sw.Stop();

            if (outcome.Status == Domain.Execution.StepStatus.Passed)
            {
                // Passed — but if prior attempts failed, this is a flake, not a
                // clean pass: FR-LOG-02 calls for Warning here specifically,
                // not Error, since the step ultimately succeeded.
                if (attempts.Count > 0)
                    _logger.LogWarning(
                        "Step passed on attempt {AttemptNumber} after {FailedAttempts} prior failed attempt(s) — flaky",
                        attemptNumber, attempts.Count);

                return new RetryOutcome(outcome, attempts);
            }

            attempts.Add(new RetryAttempt(attemptNumber, outcome.Status, sw.Elapsed, outcome.Error, DateTimeOffset.UtcNow));

            var isLastAttempt = attemptNumber == policy.MaxAttempts;
            if (isLastAttempt)
                return new RetryOutcome(outcome, attempts);

            // Not yet the final word — a routine, expected retry, not an
            // error condition: Debug, matching FR-LOG-02's level semantics.
            _logger.LogDebug("Attempt {AttemptNumber} failed, retrying after {BackoffMs}ms: {Error}",
                attemptNumber, backoff.TotalMilliseconds, outcome.Error);

            if (backoff > TimeSpan.Zero)
                await Task.Delay(backoff, ct);
            backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * policy.BackoffMultiplier);
        }

        // Unreachable given MaxAttempts >= 1, but keeps the compiler satisfied.
        return new RetryOutcome(StepOutcome.Failed("Retry policy exhausted with no attempts."), attempts);
    }
}
