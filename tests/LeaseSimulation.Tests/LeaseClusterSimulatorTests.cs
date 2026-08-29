using LeaseSimulation.Core;
using LeaseSimulation.Core.Interfaces;
using LeaseSimulation.Core.Models;

namespace LeaseSimulation.Tests;

public sealed class LeaseClusterSimulatorTests
{
    [Fact]
    public void CrashedNode_IsSuspectedAtLeaseExpiry_AndConfirmedAfterArbitration()
    {
        var simulator = CreateSimulator(arbitrationDuration: TimeSpan.FromSeconds(2));

        simulator.CrashNode(1);
        simulator.AdvanceBy(TimeSpan.FromMilliseconds(9_999));

        Assert.DoesNotContain(simulator.Leases, lease => lease.TargetId == 1 && lease.Status == LeaseStatus.Arbitrating);

        simulator.AdvanceBy(TimeSpan.FromMilliseconds(1));

        Assert.Contains(simulator.Leases, lease => lease.TargetId == 1 && lease.Status == LeaseStatus.Arbitrating);
        Assert.All(simulator.Detections.Where(item => item.TargetId == 1), item => Assert.Equal(TimeSpan.FromSeconds(10), item.SuspicionLatency));

        simulator.AdvanceBy(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(1, simulator.Topology.MemberIds);
        Assert.Contains(new RingEdgeSnapshot(0, 2), simulator.Topology.Edges);
        Assert.DoesNotContain(simulator.Leases, lease => lease.SourceId == 1 || lease.TargetId == 1);
        Assert.All(simulator.Detections.Where(item => item.TargetId == 1), item => Assert.Equal(TimeSpan.FromSeconds(12), item.ConfirmationLatency));
    }

    [Fact]
    public void HealthyNodes_RenewWithoutExpiring()
    {
        var simulator = CreateSimulator();

        simulator.AdvanceBy(TimeSpan.FromMinutes(2));

        Assert.All(simulator.Leases, lease => Assert.Equal(LeaseStatus.Active, lease.Status));
        Assert.Empty(simulator.Detections);
    }

    [Fact]
    public void RingNeighborhood_CreatesBidirectionalNearestNeighborLeases()
    {
        var simulator = CreateSimulator(nodeCount: 8, neighborhoodSize: 2);

        var observedByZero = simulator.Leases.Where(lease => lease.SourceId == 0).Select(lease => lease.TargetId);

        Assert.Equal([1, 2, 6, 7], observedByZero.Order());
        Assert.Equal(32, simulator.Leases.Count);
    }

    [Fact]
    public void ArbitrationDisabled_ConfirmsDownAtLeaseExpiry()
    {
        ILeaseClusterSimulator simulator = new LeaseClusterSimulator(new LeaseSimulationOptions
        {
            NodeCount = 5,
            NeighborhoodSize = 1,
            LeaseDuration = TimeSpan.FromSeconds(10),
            ArbitrationEnabled = false,
        });

        simulator.CrashNode(1);
        simulator.AdvanceBy(TimeSpan.FromSeconds(10));

        Assert.All(simulator.Detections.Where(item => item.TargetId == 1), item =>
        {
            Assert.Equal(TimeSpan.FromSeconds(10), item.SuspicionLatency);
            Assert.Equal(TimeSpan.FromSeconds(10), item.ConfirmationLatency);
        });
    }

    [Fact]
    public void ConfirmedFailure_ReformsNeighborhoodAroundMissingNode()
    {
        var simulator = CreateSimulator(nodeCount: 6, neighborhoodSize: 1, arbitrationDuration: TimeSpan.Zero);

        simulator.CrashNode(1);
        simulator.AdvanceBy(TimeSpan.FromSeconds(10));

        Assert.Equal([0, 2, 3, 4, 5], simulator.Topology.MemberIds);
        Assert.Contains(new RingEdgeSnapshot(0, 2), simulator.Topology.Edges);
        Assert.Equal([2, 5], simulator.Leases
            .Where(lease => lease.SourceId == 0)
            .Select(lease => lease.TargetId)
            .Order());

        simulator.RecoverNode(1);

        Assert.Equal([0, 1, 2, 3, 4, 5], simulator.Topology.MemberIds);
        Assert.Contains(new RingEdgeSnapshot(0, 1), simulator.Topology.Edges);
    }

    [Fact]
    public void ContiguousFailures_ConvergeAfterReformedNeighborhoodDetectsHiddenNode()
    {
        var simulator = CreateSimulator(nodeCount: 6, neighborhoodSize: 1, arbitrationDuration: TimeSpan.Zero);

        simulator.CrashNode(1);
        simulator.CrashNode(2);
        simulator.CrashNode(3);
        simulator.AdvanceBy(TimeSpan.FromSeconds(10));

        Assert.Equal([0, 2, 4, 5], simulator.Topology.MemberIds);
        Assert.Contains(simulator.Leases, lease =>
            lease.SourceId == 0 && lease.TargetId == 2 && lease.Status == LeaseStatus.Active);

        simulator.AdvanceBy(TimeSpan.FromSeconds(10));

        Assert.Equal([0, 4, 5], simulator.Topology.MemberIds);
        Assert.Contains(new RingEdgeSnapshot(0, 4), simulator.Topology.Edges);
        Assert.DoesNotContain(simulator.Leases, lease => lease.SourceId == 2 || lease.TargetId == 2);
    }

    [Fact]
    public void RecoveryDuringArbitration_DoesNotRemoveRecoveredNode()
    {
        var simulator = CreateSimulator(arbitrationDuration: TimeSpan.FromSeconds(5));

        simulator.CrashNode(1);
        simulator.AdvanceBy(TimeSpan.FromSeconds(10));
        simulator.RecoverNode(1);
        simulator.AdvanceBy(TimeSpan.FromSeconds(5));

        Assert.Contains(1, simulator.Topology.MemberIds);
        Assert.All(simulator.Leases.Where(lease => lease.SourceId == 1 || lease.TargetId == 1),
            lease => Assert.Equal(LeaseStatus.Active, lease.Status));
        Assert.DoesNotContain(simulator.Detections, detection => detection.TargetId == 1 && detection.ConfirmedTime is not null);
    }

    [Fact]
    public void TopologyChange_PreservesUnaffectedLeaseExpiration()
    {
        var simulator = CreateSimulator(nodeCount: 6, neighborhoodSize: 1, arbitrationDuration: TimeSpan.Zero);
        var healthyControl = CreateSimulator(nodeCount: 6, neighborhoodSize: 1, arbitrationDuration: TimeSpan.Zero);

        simulator.CrashNode(1);
        simulator.AdvanceBy(TimeSpan.FromSeconds(10));
        healthyControl.AdvanceBy(TimeSpan.FromSeconds(10));

        var remainingAfterFailure = simulator.Leases.Single(lease => lease.SourceId == 3 && lease.TargetId == 4).RemainingTtl;
        var healthyRemaining = healthyControl.Leases.Single(lease => lease.SourceId == 3 && lease.TargetId == 4).RemainingTtl;
        Assert.Equal(healthyRemaining, remainingAfterFailure);
    }

    [Fact]
    public void CrashAfterRenewal_IsDetectedWhenLastAcceptedTtlExpires()
    {
        var simulator = CreateSimulator(arbitrationDuration: TimeSpan.Zero);

        simulator.AdvanceBy(TimeSpan.FromSeconds(4));
        var remainingTtlAtCrash = simulator.Leases.Single(lease => lease.SourceId == 0 && lease.TargetId == 1).RemainingTtl;
        simulator.CrashNode(1);
        simulator.AdvanceBy(remainingTtlAtCrash - TimeSpan.FromTicks(1));

        Assert.DoesNotContain(simulator.Detections, detection => detection.SourceId == 0 && detection.TargetId == 1);

        simulator.AdvanceBy(TimeSpan.FromTicks(1));

        var detection = simulator.Detections.Single(item => item.SourceId == 0 && item.TargetId == 1);
        Assert.Equal(remainingTtlAtCrash, detection.SuspicionLatency);
        Assert.Equal(remainingTtlAtCrash, detection.ConfirmationLatency);
    }

    [Fact]
    public void AdvanceBy_IsDeterministicAcrossStepSizes()
    {
        var singleStep = CreateSimulator(nodeCount: 8, neighborhoodSize: 2, arbitrationDuration: TimeSpan.FromSeconds(2));
        var smallSteps = CreateSimulator(nodeCount: 8, neighborhoodSize: 2, arbitrationDuration: TimeSpan.FromSeconds(2));
        singleStep.CrashNode(3);
        smallSteps.CrashNode(3);

        singleStep.AdvanceBy(TimeSpan.FromSeconds(20));
        for (var index = 0; index < 200; index++)
        {
            smallSteps.AdvanceBy(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(singleStep.Elapsed, smallSteps.Elapsed);
        Assert.Equal(singleStep.Topology.Version, smallSteps.Topology.Version);
        Assert.Equal(singleStep.Topology.MemberIds, smallSteps.Topology.MemberIds);
        Assert.Equal(singleStep.Topology.Edges, smallSteps.Topology.Edges);
        Assert.Equal(singleStep.Leases, smallSteps.Leases);
        Assert.Equal(singleStep.Detections, smallSteps.Detections);
    }

    [Fact]
    public void NetworkPartition_FencesMinoritySideAfterArbitration_AndHealingRestoresIt()
    {
        var simulator = CreateSimulator(nodeCount: 7, neighborhoodSize: 1, arbitrationDuration: TimeSpan.FromSeconds(2));

        simulator.StartNetworkPartition(3);
        simulator.AdvanceBy(TimeSpan.FromMilliseconds(11_999));

        Assert.All(simulator.Nodes, node => Assert.True(node.IsRunning));

        simulator.AdvanceBy(TimeSpan.FromMilliseconds(1));

        Assert.All(simulator.Nodes.Where(node => node.Id <= 3), node => Assert.True(node.IsRunning));
        Assert.All(simulator.Nodes.Where(node => node.Id > 3), node => Assert.False(node.IsRunning));
        Assert.Equal([0, 1, 2, 3], simulator.Topology.MemberIds);

        simulator.HealNetworkPartition();

        Assert.False(simulator.IsNetworkPartitioned);
        Assert.All(simulator.Nodes, node => Assert.True(node.IsRunning));
        Assert.Equal(Enumerable.Range(0, 7), simulator.Topology.MemberIds);
    }

    [Fact]
    public void EvenNetworkPartition_FencesBothSidesWithoutVoterQuorum()
    {
        var simulator = CreateSimulator(nodeCount: 6, neighborhoodSize: 1, arbitrationDuration: TimeSpan.FromSeconds(2));

        simulator.StartNetworkPartition(2);
        simulator.AdvanceBy(TimeSpan.FromSeconds(12));

        Assert.All(simulator.Nodes, node => Assert.False(node.IsRunning));
        Assert.Empty(simulator.Topology.MemberIds);
        Assert.Contains(simulator.Events, item => item.Message.Contains("surviving side none"));
    }

    [Fact]
    public void InvalidConfiguration_IsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaseClusterSimulator(new() { NodeCount = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaseClusterSimulator(new() { NodeCount = 4, NeighborhoodSize = 4 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaseClusterSimulator(new() { LeaseDuration = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaseClusterSimulator(new() { LeaseRenewBeginRatio = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaseClusterSimulator(new() { LeaseRetryCount = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaseClusterSimulator(new() { ArbitrationDuration = TimeSpan.FromTicks(-1) }));
    }

    private static ILeaseClusterSimulator CreateSimulator(
        int nodeCount = 5,
        int neighborhoodSize = 1,
        TimeSpan? arbitrationDuration = null) => new LeaseClusterSimulator(new LeaseSimulationOptions
        {
            NodeCount = nodeCount,
            NeighborhoodSize = neighborhoodSize,
            LeaseDuration = TimeSpan.FromSeconds(10),
            ArbitrationDuration = arbitrationDuration ?? TimeSpan.FromSeconds(1),
        });
}