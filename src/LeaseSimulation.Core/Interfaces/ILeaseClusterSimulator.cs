using LeaseSimulation.Core.Models;

namespace LeaseSimulation.Core.Interfaces;

public interface ILeaseClusterSimulator
{
    TimeSpan Elapsed { get; }
    LeaseSimulationOptions Options { get; }
    IReadOnlyList<SimulationEvent> Events { get; }
    IReadOnlyList<DetectionRecord> Detections { get; }
    RingTopologySnapshot Topology { get; }
    IReadOnlyList<NodeSnapshot> Nodes { get; }
    IReadOnlyList<LeaseSnapshot> Leases { get; }
    bool IsNetworkPartitioned { get; }

    void CrashNode(int nodeId);
    void RecoverNode(int nodeId);
    void StartNetworkPartition(int splitAfterNodeId);
    void HealNetworkPartition();
    void AdvanceBy(TimeSpan duration);
}