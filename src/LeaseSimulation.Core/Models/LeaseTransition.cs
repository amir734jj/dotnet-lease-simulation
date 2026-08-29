namespace LeaseSimulation.Core.Models;

internal enum LeaseTransitionResult
{
    None,
    RenewalTimedOut,
    FailureSuspected,
    FailureCancelled,
    FailureConfirmed,
}

internal sealed record LeaseTransitionContext(
    TimeSpan Now,
    TimeSpan LeaseDuration,
    TimeSpan RenewDelay,
    int RetryCount,
    TimeSpan RetrySpacing,
    TimeSpan ArbitrationDuration);