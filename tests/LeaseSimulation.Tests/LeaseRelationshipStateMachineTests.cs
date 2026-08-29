using LeaseSimulation.Core.Models;

namespace LeaseSimulation.Tests;

public sealed class LeaseRelationshipStateMachineTests
{
    [Fact]
    public void FailedRenewal_EntersRenewing_AndSuccessReturnsToActive()
    {
        var lease = CreateLease();

        var failure = lease.Handle(LeaseEvent.RenewalFailed, Context(TimeSpan.FromSeconds(2)));

        Assert.Equal(LeaseTransitionResult.RenewalTimedOut, failure);
        Assert.Equal(LeaseStatus.Renewing, lease.Status);
        Assert.Equal(1, lease.FailedRenewals);

        var success = lease.Handle(LeaseEvent.RenewalSucceeded, Context(TimeSpan.FromSeconds(3)));

        Assert.Equal(LeaseTransitionResult.None, success);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(0, lease.FailedRenewals);
        Assert.Equal(TimeSpan.FromSeconds(13), lease.ExpiresAt);
    }

    [Fact]
    public void TtlAndArbitrationExpiry_TransitionToConfirmedDown()
    {
        var lease = CreateLease();

        var suspicion = lease.Handle(LeaseEvent.TtlExpired, Context(TimeSpan.FromSeconds(10)));
        var confirmation = lease.Handle(LeaseEvent.ArbitrationExpired, Context(TimeSpan.FromSeconds(12)));

        Assert.Equal(LeaseTransitionResult.FailureSuspected, suspicion);
        Assert.Equal(LeaseTransitionResult.FailureConfirmed, confirmation);
        Assert.Equal(LeaseStatus.Down, lease.Status);
        Assert.Equal(TimeSpan.FromSeconds(10), lease.SuspectedAt);
        Assert.Equal(TimeSpan.FromSeconds(12), lease.ConfirmedAt);
    }

    [Fact]
    public void TargetRecovery_DuringArbitration_CancelsDetection()
    {
        var lease = CreateLease();
        lease.Handle(LeaseEvent.TtlExpired, Context(TimeSpan.FromSeconds(10)));

        var result = lease.Handle(LeaseEvent.TargetRecovered, Context(TimeSpan.FromSeconds(11)));

        Assert.Equal(LeaseTransitionResult.FailureCancelled, result);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Null(lease.SuspectedAt);
        Assert.Null(lease.ConfirmedAt);
    }

    [Fact]
    public void InvalidTransition_IsRejected()
    {
        var lease = CreateLease();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            lease.Handle(LeaseEvent.ArbitrationExpired, Context(TimeSpan.FromSeconds(1))));

        Assert.Contains("Active + ArbitrationExpired", exception.Message);
    }

    private static LeaseRelationship CreateLease() => new(
        sourceId: 0,
        targetId: 1,
        expiresAt: TimeSpan.FromSeconds(10),
        nextRenewAt: TimeSpan.FromSeconds(2));

    private static LeaseTransitionContext Context(TimeSpan now) => new(
        Now: now,
        LeaseDuration: TimeSpan.FromSeconds(10),
        RenewDelay: TimeSpan.FromSeconds(2),
        RetryCount: 3,
        RetrySpacing: TimeSpan.FromSeconds(2),
        ArbitrationDuration: TimeSpan.FromSeconds(2));
}