# Windows Fabric Lease Simulation

An Avalonia desktop and WebAssembly simulation of Service Fabric federation lease failure detection. Both hosts share the same UI and deterministic simulation engine.

## Run

```powershell
dotnet run --project src/LeaseSimulation.Desktop/LeaseSimulation.Desktop.csproj
```

For the browser host, install the WebAssembly workload once:

```powershell
dotnet workload install wasm-tools
dotnet run --project src/LeaseSimulation.Browser/LeaseSimulation.Browser.csproj
```

## Docker

```powershell
docker build -t lease-simulation .
docker run --rm -p 8080:80 lease-simulation
```

Open `http://localhost:8080`.

## Test

```powershell
dotnet test LeaseSimulation.slnx
```

## Model

- Nodes form a consistent ring and monitor the nearest predecessors and successors.
- Each directed source-to-target lease has its own TTL and renewal schedule.
- Renewal begins at `LeaseDuration / LeaseRenewBeginRatio`.
- Failed renewals never extend the existing TTL.
- Expired leases enter arbitration before the target is confirmed down.
- Confirmed failures update ring membership; recovered nodes rejoin immediately.
- A network cut blocks cross-partition lease renewal. The side holding a strict majority of simulated voters survives arbitration; minority sides are fenced, and healing restarts partition-fenced nodes.
- Events and detection latency are recorded for inspection.

## Architecture

- `LeaseSimulation.Core` contains the platform-independent engine.
- `LeaseClusterSimulator` coordinates deterministic virtual time and node actors.
- Each `FederationNode` owns its outgoing leases.
- Each `LeaseRelationship` is an event-driven state machine.
- `LeaseSimulation.App` contains the shared Avalonia UI.
- `LeaseSimulation.Desktop` and `LeaseSimulation.Browser` provide the platform hosts.

The simulator is deterministic and single-threaded. It does not use wall-clock time, threads, I/O, or randomness. Callers interact through `ILeaseClusterSimulator` and advance virtual time with `AdvanceBy`.