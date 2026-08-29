namespace LeaseSimulation.Core.Models;

public sealed record NodeSnapshot(
    int Id,
    bool IsRunning,
    bool IsInRing,
    int ActiveLeases,
    int SuspectLeases,
    int DownLeases);