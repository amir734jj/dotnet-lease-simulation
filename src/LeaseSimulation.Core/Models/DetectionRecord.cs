namespace LeaseSimulation.Core.Models;

public sealed record DetectionRecord(
    int SourceId,
    int TargetId,
    TimeSpan CrashTime,
    TimeSpan SuspectedTime,
    TimeSpan? ConfirmedTime)
{
    public TimeSpan SuspicionLatency => SuspectedTime - CrashTime;
    public TimeSpan? ConfirmationLatency => ConfirmedTime - CrashTime;
}