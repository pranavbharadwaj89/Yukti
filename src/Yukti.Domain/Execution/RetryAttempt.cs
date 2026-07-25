using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Execution;

/// <summary>
/// Exists specifically to make the platform's own flake-rate metric
/// (NFR-REL-2) computable directly from the domain model — a query can
/// count FlowRuns whose final StepResult is Passed but whose RetryHistory
/// is non-empty. (Volume 1 Part II §10.4)
/// </summary>
public sealed class RetryAttempt : Entity<RetryAttemptId>
{
    public int AttemptNumber { get; }
    public StepStatus Status { get; }
    public TimeSpan Duration { get; }
    public string? Error { get; }
    public DateTimeOffset AttemptedAt { get; }

    public RetryAttempt(int attemptNumber, StepStatus status, TimeSpan duration, string? error, DateTimeOffset attemptedAt)
        : base(new RetryAttemptId(Guid.NewGuid()))
    {
        AttemptNumber = attemptNumber;
        Status = status;
        Duration = duration;
        Error = error;
        AttemptedAt = attemptedAt;
    }
}
