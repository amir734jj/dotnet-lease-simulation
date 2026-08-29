namespace LeaseSimulation.Core.Models;

public sealed record SimulationEvent(
    TimeSpan Time,
    string Message,
    int? SourceId = null,
    int? TargetId = null);