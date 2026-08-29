namespace LeaseSimulation.Core.Models;

/// <summary>
/// Defines immutable timing and topology inputs for a lease simulation.
/// </summary>
public sealed record LeaseSimulationOptions
{
    public int NodeCount { get; init; } = 12;
    public int NeighborhoodSize { get; init; } = 4;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public int LeaseRenewBeginRatio { get; init; } = 6;
    public int LeaseRetryCount { get; init; } = 3;
    public bool ArbitrationEnabled { get; init; } = true;
    public TimeSpan ArbitrationDuration { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(NodeCount, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(NeighborhoodSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(NeighborhoodSize, NodeCount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(LeaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(LeaseRenewBeginRatio, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(LeaseRetryCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ArbitrationDuration, TimeSpan.Zero);
    }
}