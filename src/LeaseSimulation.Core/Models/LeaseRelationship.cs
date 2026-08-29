namespace LeaseSimulation.Core.Models;

internal sealed class LeaseRelationship(int sourceId, int targetId, TimeSpan expiresAt, TimeSpan nextRenewAt)
{
    public int SourceId { get; } = sourceId;
    public int TargetId { get; } = targetId;
    public LeaseStatus Status { get; private set; } = LeaseStatus.Active;
    public TimeSpan ExpiresAt { get; private set; } = expiresAt;
    public TimeSpan NextRenewAt { get; private set; } = nextRenewAt;
    public TimeSpan ArbitrationEndsAt { get; private set; } = TimeSpan.MaxValue;
    public int FailedRenewals { get; private set; }
    public TimeSpan? SuspectedAt { get; private set; }
    public TimeSpan? ConfirmedAt { get; private set; }

    public LeaseTransitionResult Handle(LeaseEvent leaseEvent, LeaseTransitionContext context)
    {
        return (Status, leaseEvent) switch
        {
            (LeaseStatus.Active or LeaseStatus.Renewing, LeaseEvent.RenewalSucceeded) => Renew(context),
            (LeaseStatus.Active or LeaseStatus.Renewing, LeaseEvent.RenewalFailed) => RecordFailedRenewal(context),
            (LeaseStatus.Active or LeaseStatus.Renewing, LeaseEvent.TtlExpired) => BeginArbitration(context),
            (LeaseStatus.Arbitrating, LeaseEvent.ArbitrationExpired) => ConfirmDown(context),
            (LeaseStatus.Arbitrating, LeaseEvent.TargetRecovered) => CancelFailureDetection(context),
            (LeaseStatus.Active or LeaseStatus.Renewing, LeaseEvent.TargetRecovered) => Reset(context),
            (LeaseStatus.Active or LeaseStatus.Renewing, LeaseEvent.SourceStopped) => Deactivate(),
            (_, LeaseEvent.Reestablished) => Reset(context),
            _ => throw new InvalidOperationException(
                $"Invalid lease transition for {SourceId}->{TargetId}: {Status} + {leaseEvent}"),
        };
    }

    private LeaseTransitionResult Renew(LeaseTransitionContext context)
    {
        Status = LeaseStatus.Active;
        FailedRenewals = 0;
        ExpiresAt = context.Now + context.LeaseDuration;
        NextRenewAt = context.Now + context.RenewDelay;
        return LeaseTransitionResult.None;
    }

    private LeaseTransitionResult RecordFailedRenewal(LeaseTransitionContext context)
    {
        Status = LeaseStatus.Renewing;
        FailedRenewals++;
        NextRenewAt = FailedRenewals >= context.RetryCount
            ? TimeSpan.MaxValue
            : context.Now + context.RetrySpacing;
        return LeaseTransitionResult.RenewalTimedOut;
    }

    private LeaseTransitionResult BeginArbitration(LeaseTransitionContext context)
    {
        SuspectedAt = context.Now;
        Status = LeaseStatus.Arbitrating;
        ArbitrationEndsAt = context.Now + context.ArbitrationDuration;
        return LeaseTransitionResult.FailureSuspected;
    }

    private LeaseTransitionResult ConfirmDown(LeaseTransitionContext context)
    {
        Status = LeaseStatus.Down;
        ConfirmedAt = context.Now;
        return LeaseTransitionResult.FailureConfirmed;
    }

    private LeaseTransitionResult CancelFailureDetection(LeaseTransitionContext context)
    {
        Reset(context);
        return LeaseTransitionResult.FailureCancelled;
    }

    private LeaseTransitionResult Deactivate()
    {
        Status = LeaseStatus.Down;
        return LeaseTransitionResult.None;
    }

    private LeaseTransitionResult Reset(LeaseTransitionContext context)
    {
        Renew(context);
        ArbitrationEndsAt = TimeSpan.MaxValue;
        SuspectedAt = null;
        ConfirmedAt = null;
        return LeaseTransitionResult.None;
    }

    public LeaseSnapshot CreateSnapshot(TimeSpan now) => new(
        SourceId,
        TargetId,
        Status,
        Status == LeaseStatus.Down ? TimeSpan.Zero : Max(TimeSpan.Zero, ExpiresAt - now),
        FailedRenewals,
        SuspectedAt,
        ConfirmedAt);

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}