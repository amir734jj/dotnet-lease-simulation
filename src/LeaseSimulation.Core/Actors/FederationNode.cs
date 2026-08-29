using LeaseSimulation.Core.Interfaces;
using LeaseSimulation.Core.Models;

namespace LeaseSimulation.Core.Actors;

internal sealed class FederationNode(int id) : IFederationNodeActor
{
    private readonly List<LeaseRelationship> outgoingLeases = [];

    public int Id { get; } = id;
    public bool IsRunning { get; private set; } = true;
    public bool IsInRing { get; private set; } = true;
    public TimeSpan? CrashedAt { get; private set; }
    public IReadOnlyList<LeaseRelationship> OutgoingLeases => outgoingLeases;

    public bool Crash(TimeSpan time)
    {
        if (!IsRunning)
        {
            return false;
        }

        IsRunning = false;
        CrashedAt = time;
        return true;
    }

    public bool Recover()
    {
        if (IsRunning)
        {
            return false;
        }

        IsRunning = true;
        return true;
    }

    public bool JoinRing()
    {
        if (IsInRing)
        {
            return false;
        }

        IsInRing = true;
        return true;
    }

    public void RemoveFromRing() => IsInRing = false;

    public void ReconcileTargets(IEnumerable<int> targetIds, TimeSpan now, LeaseSimulationOptions options)
    {
        var targets = targetIds.ToHashSet();
        outgoingLeases.RemoveAll(lease => !targets.Contains(lease.TargetId));

        var existingTargets = outgoingLeases.Select(lease => lease.TargetId).ToHashSet();
        foreach (var targetId in targets.Except(existingTargets).Order())
        {
            outgoingLeases.Add(new LeaseRelationship(
                Id,
                targetId,
                now + options.LeaseDuration,
                now + RenewDelay(options)));
        }
    }

    public void ResetAllLeases(TimeSpan now, LeaseSimulationOptions options)
    {
        foreach (var lease in outgoingLeases)
        {
            lease.Handle(LeaseEvent.Reestablished, CreateTransitionContext(now, options));
        }
    }

    public void ResetLeaseTo(int targetId, TimeSpan now, LeaseSimulationOptions options)
    {
        var lease = outgoingLeases.SingleOrDefault(item => item.TargetId == targetId);
        lease?.Handle(LeaseEvent.Reestablished, CreateTransitionContext(now, options));
    }

    public TimeSpan NextDueTime()
    {
        var nextDue = TimeSpan.MaxValue;
        foreach (var lease in outgoingLeases)
        {
            var due = lease.Status switch
            {
                LeaseStatus.Active or LeaseStatus.Renewing => Min(lease.NextRenewAt, lease.ExpiresAt),
                LeaseStatus.Arbitrating => lease.ArbitrationEndsAt,
                _ => TimeSpan.MaxValue,
            };
            nextDue = Min(nextDue, due);
        }

        return nextDue;
    }

    public IReadOnlyList<NodeLeaseOutcome> ProcessDueTransitions(
        TimeSpan now,
        LeaseSimulationOptions options,
        Func<int, IFederationNodeActor> resolveNode)
    {
        var outcomes = new List<NodeLeaseOutcome>();
        foreach (var lease in outgoingLeases.ToArray())
        {
            if (lease.Status is LeaseStatus.Active or LeaseStatus.Renewing)
            {
                if (lease.NextRenewAt <= now && lease.NextRenewAt < lease.ExpiresAt)
                {
                    ProcessRenewal(lease, now, options, resolveNode(lease.TargetId), outcomes);
                }

                if (lease.ExpiresAt <= now)
                {
                    ProcessExpiration(lease, now, options, resolveNode(lease.TargetId), outcomes);
                }
            }

            if (lease.Status == LeaseStatus.Arbitrating && lease.ArbitrationEndsAt <= now)
            {
                CompleteArbitration(lease, now, options, resolveNode(lease.TargetId), outcomes);
            }
        }

        return outcomes;
    }

    public NodeSnapshot CreateSnapshot() => new(
        Id,
        IsRunning,
        IsInRing,
        outgoingLeases.Count(lease => lease.Status is LeaseStatus.Active or LeaseStatus.Renewing),
        outgoingLeases.Count(lease => lease.Status == LeaseStatus.Arbitrating),
        outgoingLeases.Count(lease => lease.Status == LeaseStatus.Down));

    private void ProcessRenewal(
        LeaseRelationship lease,
        TimeSpan now,
        LeaseSimulationOptions options,
        IFederationNodeActor target,
        ICollection<NodeLeaseOutcome> outcomes)
    {
        if (IsRunning && target.IsRunning)
        {
            lease.Handle(LeaseEvent.RenewalSucceeded, CreateTransitionContext(now, options));
            return;
        }

        Dispatch(
            lease.Handle(LeaseEvent.RenewalFailed, CreateTransitionContext(now, options)),
            target,
            outcomes);
    }

    private void ProcessExpiration(
        LeaseRelationship lease,
        TimeSpan now,
        LeaseSimulationOptions options,
        IFederationNodeActor target,
        ICollection<NodeLeaseOutcome> outcomes)
    {
        if (!IsRunning)
        {
            lease.Handle(LeaseEvent.SourceStopped, CreateTransitionContext(now, options));
            return;
        }

        if (target.IsRunning)
        {
            lease.Handle(LeaseEvent.TargetRecovered, CreateTransitionContext(now, options));
            return;
        }

        Dispatch(
            lease.Handle(LeaseEvent.TtlExpired, CreateTransitionContext(now, options)),
            target,
            outcomes);
        if (!options.ArbitrationEnabled || options.ArbitrationDuration == TimeSpan.Zero)
        {
            CompleteArbitration(lease, now, options, target, outcomes);
        }
    }

    private void CompleteArbitration(
        LeaseRelationship lease,
        TimeSpan now,
        LeaseSimulationOptions options,
        IFederationNodeActor target,
        ICollection<NodeLeaseOutcome> outcomes)
    {
        if (lease.Status == LeaseStatus.Down)
        {
            return;
        }

        if (target.IsRunning)
        {
            Dispatch(
                lease.Handle(LeaseEvent.TargetRecovered, CreateTransitionContext(now, options)),
                target,
                outcomes);
            return;
        }

        Dispatch(
            lease.Handle(LeaseEvent.ArbitrationExpired, CreateTransitionContext(now, options)),
            target,
            outcomes);
    }

    private void Dispatch(
        LeaseTransitionResult result,
        IFederationNodeActor target,
        ICollection<NodeLeaseOutcome> outcomes)
    {
        var outcome = result switch
        {
            LeaseTransitionResult.None => null,
            LeaseTransitionResult.RenewalTimedOut => new NodeLeaseOutcome(
                NodeLeaseOutcomeKind.RenewalTimedOut, Id, target.Id),
            LeaseTransitionResult.FailureSuspected => new NodeLeaseOutcome(
                NodeLeaseOutcomeKind.FailureSuspected, Id, target.Id, target.CrashedAt),
            LeaseTransitionResult.FailureCancelled => new NodeLeaseOutcome(
                NodeLeaseOutcomeKind.FailureCancelled, Id, target.Id),
            LeaseTransitionResult.FailureConfirmed => new NodeLeaseOutcome(
                NodeLeaseOutcomeKind.FailureConfirmed, Id, target.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };

        if (outcome is not null)
        {
            outcomes.Add(outcome);
        }
    }

    private static LeaseTransitionContext CreateTransitionContext(
        TimeSpan now,
        LeaseSimulationOptions options)
    {
        var renewDelay = RenewDelay(options);
        return new LeaseTransitionContext(
            now,
            options.LeaseDuration,
            renewDelay,
            options.LeaseRetryCount,
            (options.LeaseDuration - renewDelay) / options.LeaseRetryCount,
            options.ArbitrationDuration);
    }

    private static TimeSpan RenewDelay(LeaseSimulationOptions options) =>
        options.LeaseDuration / options.LeaseRenewBeginRatio;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}