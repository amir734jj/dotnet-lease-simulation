namespace LeaseSimulation.Core.Models;

internal enum LeaseEvent
{
    RenewalSucceeded,
    RenewalFailed,
    TtlExpired,
    ArbitrationExpired,
    TargetRecovered,
    SourceStopped,
    Reestablished,
}