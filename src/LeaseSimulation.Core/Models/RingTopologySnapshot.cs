namespace LeaseSimulation.Core.Models;

public sealed record RingTopologySnapshot(
    int Version,
    IReadOnlyList<int> MemberIds,
    IReadOnlyList<RingEdgeSnapshot> Edges);