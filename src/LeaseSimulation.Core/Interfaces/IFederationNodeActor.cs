using LeaseSimulation.Core.Models;

namespace LeaseSimulation.Core.Interfaces;

internal interface IFederationNodeActor
{
    int Id { get; }
    bool IsRunning { get; }
    bool IsInRing { get; }
    TimeSpan? CrashedAt { get; }
    IReadOnlyList<LeaseRelationship> OutgoingLeases { get; }

    bool Crash(TimeSpan time);
    bool Recover();
    bool JoinRing();
    void RemoveFromRing();
    void ReconcileTargets(IEnumerable<int> targetIds, TimeSpan now, LeaseSimulationOptions options);
    void ResetAllLeases(TimeSpan now, LeaseSimulationOptions options);
    void ResetLeaseTo(int targetId, TimeSpan now, LeaseSimulationOptions options);
    TimeSpan NextDueTime();
    IReadOnlyList<NodeLeaseOutcome> ProcessDueTransitions(
        TimeSpan now,
        LeaseSimulationOptions options,
        Func<int, IFederationNodeActor> resolveNode,
        Func<int, int, bool> canCommunicate,
        Func<int, int, TimeSpan?> getCommunicationFailureTime);
    NodeSnapshot CreateSnapshot();
}