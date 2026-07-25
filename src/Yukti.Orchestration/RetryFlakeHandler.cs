using System.Diagnostics;
using Yukti.Contracts;
using Yukti.Domain.Execution;

namespace Yukti.Orchestration;

public sealed class RetryFlakeHandler : IRetryFlakeHandler
{
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
                // Passed — but if prior attempts failed, this is a flake, not a clean pass.
                return new RetryOutcome(outcome, attempts);
            }

            attempts.Add(new RetryAttempt(attemptNumber, outcome.Status, sw.Elapsed, outcome.Error, DateTimeOffset.UtcNow));

            var isLastAttempt = attemptNumber == policy.MaxAttempts;
            if (isLastAttempt)
                return new RetryOutcome(outcome, attempts);

            if (backoff > TimeSpan.Zero)
                await Task.Delay(backoff, ct);
            backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * policy.BackoffMultiplier);
        }

        // Unreachable given MaxAttempts >= 1, but keeps the compiler satisfied.
        return new RetryOutcome(StepOutcome.Failed("Retry policy exhausted with no attempts."), attempts);
    }
}
