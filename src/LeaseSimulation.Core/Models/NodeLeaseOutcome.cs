namespace LeaseSimulation.Core.Models;

internal enum NodeLeaseOutcomeKind
{
    RenewalTimedOut,
    FailureSuspected,
    FailureCancelled,
    FailureConfirmed,
}

internal sealed record NodeLeaseOutcome(
    NodeLeaseOutcomeKind Kind,
    int SourceId,
    int TargetId,
    TimeSpan? TargetCrashTime = null);