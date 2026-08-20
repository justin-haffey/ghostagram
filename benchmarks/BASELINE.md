# Ghostagram graph-upgrade benchmark baseline

This baseline records the upgraded System-graph bridge and existing browser-model interaction workloads. It is retained evidence for comparison, not a portable performance guarantee.

- Captured: 2026-08-20 UTC
- Machine: `STATION`
- OS: Microsoft Windows 10.0.26200
- .NET runtime: 10.0.10
- Node.js: 24.18.0

## System graph projection

Reproduce from the repository root:

```powershell
dotnet run --project .\benchmarks\Ghostagram.Bridge.Benchmarks\Ghostagram.Bridge.Benchmarks.csproj -c Release --no-build -- --repetitions 5 --sizes 100,500,1000
```

| Nodes | Edges | Repetitions | Full mean ms | Incremental mean ms |
| ---: | ---: | ---: | ---: | ---: |
| 100 | 99 | 5 | 0.652 | 0.144 |
| 500 | 499 | 5 | 3.231 | 0.609 |
| 1,000 | 999 | 5 | 6.740 | 1.252 |

The incremental workload projects one node rename and validates the result against the authoritative graph snapshot. The executable also emits a `ghostagram.bridge.benchmark.v1` JSON record.

## Browser-model interaction

Reproduce from `src/Ghostagram`:

```powershell
npm.cmd run benchmark
```

| Workload | Graph size | Result |
| --- | --- | ---: |
| One-node delta | 1,000 nodes / 2,000 edges | 0.189 ms |
| Obstacle-aware lasso | 1,000 nodes / 2,000 edges | 83.545 ms |
| Lasso selection count | 1,000 nodes / 2,000 edges | 783 |

The benchmark reported one dirty node and four dirty edges for the delta. Its existing 100 ms gates passed. This model-level harness complements but does not replace visible-browser rendering and console verification.
