using System.Collections.ObjectModel;
using LeaseSimulation.Core.Actors;
using LeaseSimulation.Core.Interfaces;
using LeaseSimulation.Core.Models;

namespace LeaseSimulation.Core;

/// <summary>
/// Runs a deterministic, single-threaded federation lease model using caller-controlled virtual time.
/// </summary>
public sealed class LeaseClusterSimulator : ILeaseClusterSimulator
{
    private readonly LeaseSimulationOptions options;
    private readonly IReadOnlyList<IFederationNodeActor> nodes;
    private readonly List<SimulationEvent> events = [];
    private readonly List<DetectionRecord> detections = [];
    private readonly ReadOnlyCollection<SimulationEvent> eventView;
    private readonly ReadOnlyCollection<DetectionRecord> detectionView;
    private readonly HashSet<int> pendingRingRemovals = [];
    private readonly HashSet<int> partitionFencedNodes = [];
    private int? partitionAfterNodeId;
    private TimeSpan? partitionStartedAt;
    private TimeSpan? partitionResolutionAt;
    private int topologyVersion = 1;

    public LeaseClusterSimulator(LeaseSimulationOptions options)
    {
        options.Validate();
        this.options = options;
        nodes = Enumerable.Range(0, options.NodeCount).Select(id => new FederationNode(id)).ToArray();
        eventView = events.AsReadOnly();
        detectionView = detections.AsReadOnly();

        ReconcileRingNeighborhood();
        events.Add(new(TimeSpan.Zero, $"Federation started with {options.NodeCount} nodes"));
    }

    public TimeSpan Elapsed { get; private set; }
    public LeaseSimulationOptions Options => options;
    public IReadOnlyList<SimulationEvent> Events => eventView;
    public IReadOnlyList<DetectionRecord> Detections => detectionView;
    public RingTopologySnapshot Topology => CreateTopologySnapshot();
    public bool IsNetworkPartitioned => partitionAfterNodeId is not null;

    public IReadOnlyList<NodeSnapshot> Nodes => nodes.Select(node => node.CreateSnapshot()).ToArray();

    public IReadOnlyList<LeaseSnapshot> Leases => nodes
        .SelectMany(node => node.OutgoingLeases)
        .Select(lease => lease.CreateSnapshot(Elapsed))
        .ToArray();

    /// <summary>
    /// Stops a node at the current virtual time without immediately changing ring membership.
    /// </summary>
    public void CrashNode(int nodeId)
    {
        var node = GetNode(nodeId);
        if (!node.Crash(Elapsed))
        {
            return;
        }

        events.Add(new(Elapsed, $"Node {nodeId:00} crashed", TargetId: nodeId));
    }

    /// <summary>
    /// Restarts a node and immediately re-establishes its incident leases and ring membership.
    /// </summary>
    public void RecoverNode(int nodeId)
    {
        var node = GetNode(nodeId);
        if (!node.Recover())
        {
            return;
        }

        pendingRingRemovals.Remove(nodeId);
        if (node.JoinRing())
        {
            topologyVersion++;
            ReconcileRingNeighborhood();
            events.Add(new(Elapsed, $"Ring v{topologyVersion} formed: {FormatRingMembers()}", TargetId: nodeId));
        }

        node.ResetAllLeases(Elapsed, options);
        foreach (var source in nodes.Where(item => item.Id != nodeId))
        {
            source.ResetLeaseTo(nodeId, Elapsed, options);
        }

        events.Add(new(Elapsed, $"Node {nodeId:00} recovered; leases re-established", TargetId: nodeId));
    }

    public void StartNetworkPartition(int splitAfterNodeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(splitAfterNodeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(splitAfterNodeId, nodes.Count - 1);
        if (IsNetworkPartitioned)
        {
            return;
        }

        partitionAfterNodeId = splitAfterNodeId;
        partitionStartedAt = Elapsed;
        var firstCrossPartitionExpiry = nodes
            .SelectMany(node => node.OutgoingLeases)
            .Where(lease => !CanCommunicate(lease.SourceId, lease.TargetId))
            .Select(lease => lease.ExpiresAt)
            .DefaultIfEmpty(Elapsed + options.LeaseDuration)
            .Min();
        partitionResolutionAt = firstCrossPartitionExpiry +
            (options.ArbitrationEnabled ? options.ArbitrationDuration : TimeSpan.Zero);

        events.Add(new(Elapsed,
            $"Network partition started: 00-{splitAfterNodeId:00} | {splitAfterNodeId + 1:00}-{nodes.Count - 1:00}"));
    }

    public void HealNetworkPartition()
    {
        if (!IsNetworkPartitioned)
        {
            return;
        }

        partitionAfterNodeId = null;
        partitionStartedAt = null;
        partitionResolutionAt = null;
        foreach (var nodeId in partitionFencedNodes)
        {
            nodes[nodeId].Recover();
            nodes[nodeId].JoinRing();
            pendingRingRemovals.Remove(nodeId);
        }

        partitionFencedNodes.Clear();
        topologyVersion++;
        ReconcileRingNeighborhood();
        foreach (var node in nodes.Where(node => node.IsRunning))
        {
            node.ResetAllLeases(Elapsed, options);
        }

        events.Add(new(Elapsed, $"Network partition healed; ring v{topologyVersion} restored"));
    }

    /// <summary>
    /// Advances virtual time and processes every due transition in chronological order.
    /// </summary>
    public void AdvanceBy(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        var target = Elapsed + duration;

        while (TryGetNextDueTime(target, out var nextDue))
        {
            Elapsed = nextDue;
            ProcessDueTransitions();
        }

        Elapsed = target;
    }

    private void ReconcileRingNeighborhood()
    {
        var members = nodes.Where(node => node.IsInRing).Select(node => node.Id).ToArray();
        foreach (var node in nodes.Where(node => !node.IsInRing))
        {
            node.ReconcileTargets([], Elapsed, options);
        }

        for (var sourceIndex = 0; sourceIndex < members.Length; sourceIndex++)
        {
            var sourceId = members[sourceIndex];
            var targets = new HashSet<int>();
            var maximumDistance = Math.Min(options.NeighborhoodSize, members.Length - 1);
            for (var distance = 1; distance <= maximumDistance; distance++)
            {
                var successorIndex = (sourceIndex + distance) % members.Length;
                var predecessorIndex = (sourceIndex - distance + members.Length) % members.Length;
                targets.Add(members[successorIndex]);
                targets.Add(members[predecessorIndex]);
            }

            nodes[sourceId].ReconcileTargets(targets, Elapsed, options);
        }
    }

    private bool TryGetNextDueTime(TimeSpan target, out TimeSpan nextDue)
    {
        nextDue = partitionResolutionAt is not null && partitionResolutionAt <= target
            ? partitionResolutionAt.Value
            : TimeSpan.MaxValue;
        foreach (var due in nodes.Select(node => node.NextDueTime()))
        {
            if (due <= target && due < nextDue)
            {
                nextDue = due;
            }
        }

        return nextDue != TimeSpan.MaxValue;
    }

    private void ProcessDueTransitions()
    {
        ResolveNetworkPartition();
        foreach (var node in nodes)
        {
            foreach (var outcome in node.ProcessDueTransitions(
                         Elapsed,
                         options,
                         GetNode,
                         CanCommunicate,
                         GetCommunicationFailureTime))
            {
                RecordOutcome(outcome);
            }
        }

        ApplyPendingRingChanges();
    }

    private void RecordOutcome(NodeLeaseOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case NodeLeaseOutcomeKind.RenewalTimedOut:
                events.Add(new(Elapsed,
                    $"Node {outcome.SourceId:00} renewal to {outcome.TargetId:00} timed out",
                    outcome.SourceId, outcome.TargetId));
                break;
            case NodeLeaseOutcomeKind.FailureSuspected:
                events.Add(new(Elapsed,
                    $"Node {outcome.SourceId:00} suspects {outcome.TargetId:00}; lease TTL expired",
                    outcome.SourceId, outcome.TargetId));
                detections.Add(new(
                    outcome.SourceId,
                    outcome.TargetId,
                    outcome.FailureStartedAt ?? throw new InvalidOperationException(
                        $"Lease {outcome.SourceId}->{outcome.TargetId} has no failure start time."),
                    Elapsed,
                    null));
                break;
            case NodeLeaseOutcomeKind.FailureCancelled:
                events.Add(new(Elapsed,
                    $"Node {outcome.SourceId:00} cancels failure detection for recovered node {outcome.TargetId:00}",
                    outcome.SourceId, outcome.TargetId));
                break;
            case NodeLeaseOutcomeKind.FailureConfirmed:
                var index = detections.FindLastIndex(item =>
                    item.SourceId == outcome.SourceId && item.TargetId == outcome.TargetId);
                if (index >= 0)
                {
                    detections[index] = detections[index] with { ConfirmedTime = Elapsed };
                }

                events.Add(new(Elapsed,
                    $"Node {outcome.SourceId:00} confirms {outcome.TargetId:00} down",
                    outcome.SourceId, outcome.TargetId));
                if (!nodes[outcome.TargetId].IsRunning && nodes[outcome.TargetId].IsInRing)
                {
                    pendingRingRemovals.Add(outcome.TargetId);
                }
                break;
        }
    }

    private void ApplyPendingRingChanges()
    {
        if (pendingRingRemovals.Count == 0)
        {
            return;
        }

        foreach (var nodeId in pendingRingRemovals)
        {
            nodes[nodeId].RemoveFromRing();
        }

        pendingRingRemovals.Clear();
        topologyVersion++;
        ReconcileRingNeighborhood();
        events.Add(new(Elapsed, $"Ring v{topologyVersion} formed: {FormatRingMembers()}"));
    }

    private RingTopologySnapshot CreateTopologySnapshot()
    {
        var members = nodes.Where(node => node.IsInRing).Select(node => node.Id).ToArray();
        var edgeCount = members.Length == 2 ? 1 : members.Length;
        var edges = members.Length < 2
            ? []
            : Enumerable.Range(0, edgeCount)
                .Select(index => new RingEdgeSnapshot(members[index], members[(index + 1) % members.Length]))
                .ToArray();
        return new RingTopologySnapshot(topologyVersion, members, edges);
    }

    private string FormatRingMembers() =>
        string.Join(" -> ", Topology.MemberIds.Select(nodeId => nodeId.ToString("00")));

    private void ResolveNetworkPartition()
    {
        if (partitionAfterNodeId is null || partitionResolutionAt is null || partitionResolutionAt > Elapsed)
        {
            return;
        }

        var split = partitionAfterNodeId.Value;
        var quorum = nodes.Count / 2 + 1;
        var leftHasQuorum = split + 1 >= quorum;
        var rightHasQuorum = nodes.Count - split - 1 >= quorum;
        foreach (var node in nodes.Where(node => node.IsRunning))
        {
            var isLeft = node.Id <= split;
            if ((isLeft && leftHasQuorum) || (!isLeft && rightHasQuorum))
            {
                continue;
            }

            node.Crash(Elapsed);
            partitionFencedNodes.Add(node.Id);
            pendingRingRemovals.Add(node.Id);
        }

        var survivor = leftHasQuorum
            ? $"00-{split:00}"
            : rightHasQuorum
                ? $"{split + 1:00}-{nodes.Count - 1:00}"
                : "none";
        events.Add(new(Elapsed,
            $"Partition arbitration completed: quorum {quorum}/{nodes.Count}, surviving side {survivor}; {partitionFencedNodes.Count} nodes fenced"));
        partitionResolutionAt = null;
    }

    private bool CanCommunicate(int sourceId, int targetId) =>
        partitionAfterNodeId is null ||
        (sourceId <= partitionAfterNodeId.Value) == (targetId <= partitionAfterNodeId.Value);

    private TimeSpan? GetCommunicationFailureTime(int sourceId, int targetId) =>
        CanCommunicate(sourceId, targetId) ? null : partitionStartedAt;

    private IFederationNodeActor GetNode(int nodeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nodeId, nodes.Count);
        return nodes[nodeId];
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}