namespace LeaseSimulation.Core.Models;

public sealed record LeaseSnapshot(
    int SourceId,
    int TargetId,
    LeaseStatus Status,
    TimeSpan RemainingTtl,
    int FailedRenewals,
    TimeSpan? SuspectedAt,
    TimeSpan? ConfirmedAt);