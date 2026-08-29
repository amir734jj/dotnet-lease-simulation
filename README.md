# Windows Fabric Lease Simulation

An Avalonia desktop and WebAssembly simulation of the Service Fabric federation lease failure-detection path. The simulation engine is a dependency-free C# class library; both hosts reuse the same UI.

## Run

Run the desktop host:

```powershell
dotnet run --project src/LeaseSimulation.Desktop/LeaseSimulation.Desktop.csproj
```

Install the WebAssembly toolchain once, then run the browser host:

```powershell
dotnet workload install wasm-tools
dotnet run --project src/LeaseSimulation.Browser/LeaseSimulation.Browser.csproj
```

Build and run the static web application in an Alpine container:

```powershell
docker build -t lease-simulation .
docker run --rm -p 8080:80 lease-simulation
```

Open `http://localhost:8080`.

Run the engine tests with:

```powershell
dotnet test LeaseSimulation.slnx
```

## What is modeled

- Nodes form a consistent ring and monitor the nearest predecessors and successors.
- Every source-to-target relationship has its own TTL.
- Renewal begins at `LeaseDuration / LeaseRenewBeginRatio`.
- Failed renewals never extend the existing TTL.
- Expiration moves a lease into arbitration, followed by a confirmed-down result.
- Crashes, recoveries, failed renewals, suspicions, confirmations, and detection latencies are recorded.
- Simulation time advances deterministically, independently of wall-clock speed.

The main algorithm is `LeaseClusterSimulator` in `src/LeaseSimulation.Core`. Consumers depend on `ILeaseClusterSimulator`; the concrete class is created only at the application composition boundary. The engine has no Avalonia, timer, or platform dependencies.

## Core contract

`LeaseClusterSimulator` is deterministic and single-threaded. It never reads wall-clock time, starts threads, performs I/O, or uses randomness. The caller controls virtual time exclusively through `AdvanceBy`.

The engine uses a deterministic actor model. Each `FederationNode` implements the internal `IFederationNodeActor` contract, owns its outgoing leases, calculates its next due time, processes renewals and arbitration, and emits outcome messages such as suspicion or confirmation. `LeaseRelationship` owns the individual lease state machine. `LeaseClusterSimulator` depends only on node actor interfaces and acts as the virtual-time scheduler and federation coordinator: it assigns ring neighborhoods, dispatches due-time turns, records emitted outcomes, and applies membership decisions. Nodes do not receive dedicated threads, so equivalent virtual-time inputs remain reproducible. Application code uses the public `ILeaseClusterSimulator` contract and cannot directly mutate actors.

The state machine for each directed source-to-target lease is:

1. `Active`: a successful renewal sets expiry to `now + LeaseDuration` and schedules the next renewal at `now + LeaseDuration / LeaseRenewBeginRatio`.
2. `Renewing`: a failed renewal does not extend expiry. Up to `LeaseRetryCount` attempts are distributed through the remaining lease interval.
3. `Arbitrating`: the TTL expired while the source is alive and the target is down. Arbitration can be disabled for immediate confirmation.
4. `Down`: arbitration confirmed the target failure. The target is removed from centralized simulated membership.

`LeaseRelationship` exposes one event-driven transition entry point. Node actors translate observations into events such as `RenewalSucceeded`, `RenewalFailed`, `TtlExpired`, `ArbitrationExpired`, and `TargetRecovered`. The FSM returns a transition result that the actor publishes as an outcome message. Invalid state/event combinations throw immediately. This small domain-specific FSM is implemented locally rather than depending on an external workflow-oriented FSM package.

Ring membership changes only after confirmed failure, not at the instant of a crash. The remaining node IDs retain their ordering, nearest-neighbor relationships are reconciled around the gap, unaffected leases retain their existing TTLs, and only new relationships receive a fresh lease. Recovery rejoins the node immediately and re-establishes only incident relationships.

The simulator returns snapshots and read-only event/detection views. It is not thread-safe; one owner should issue commands and read snapshots. Advancing by one large interval is guaranteed to produce the same state as equivalent smaller intervals.

## Windows Fabric source mapping

The implementation was derived from these areas in `C:\repos\WindowsFabric`:

- `src\prod\src\Federation\FederationConfig.h`: neighborhood size 4, lease duration 30 seconds, renew ratio 6, retry count 3, suspend timeout 2 seconds, and arbitration timeout 30 seconds.
- `src\prod\src\LeaseAgent\LeasePartner.cpp`: lease states and establish/retry/fail transitions.
- `src\prod\src\LeaseAgent\LeaseAgent.cpp`: lease-layer registration, callback wiring, and configuration transfer.
- `src\prod\src\Federation\ArbitrationOperation.cpp`: arbitration expiry and voter requests.
- `src\prod\src\Lease\sys\LeasLayr.c` and `Lease\UserMode`: native subject/monitor TTL renewal and expiration behavior. This simulator calls those roles target/source.

This is a behavioral simulator, not a port of the kernel lease driver. Membership is centralized and changes when the first live source confirms failure. Network transport, clock uncertainty, indirect leases, voter quorum decisions, fault-domain lease durations, and process termination are represented by deterministic state transitions or omitted. Arbitration duration is configurable and represents the time until a decision; the native implementation can finish earlier when voter replies arrive.