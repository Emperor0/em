# D7 BLACKCORE Next — Architecture

## Product boundary

D7 BLACKCORE is a Windows gaming performance orchestrator, not a generic tweak pack. Every optimization follows:

`Scan -> Diagnose -> Baseline -> Evidence -> Backup -> Apply -> Verify -> Measure -> Compare -> Keep/Rollback -> Learn`

## Runtime strategy

- Main application: C#/.NET WPF, Arabic RTL, x64, self-contained publish.
- Native performance workers: Rust only where a measured benefit exists (high-frequency telemetry, low-overhead IPC/native workers).
- C++ only where a vendor SDK/Windows native API is materially easier or only available through C/C++.
- PowerShell: development/support diagnostics only; never the production core.
- Python: CI/research/benchmark analysis only; never a user runtime requirement.

The first development target is .NET 10 LTS with Windows desktop targeting. Windows 10 Pro build 19045 is outside current official Windows lifecycle support, therefore actual execution compatibility on build 19045 is an explicit release gate. The product publishes self-contained x64 and does not assume a machine-wide .NET runtime.

## Solution layout

```text
d7-blackcore-next/
  src/
    D7.App/             # WPF shell, Arabic RTL UX, composition root
    D7.Core/            # contracts, domain models, decisions, capability states
    D7.Hardware/        # one-time discovery + capability probes
    D7.Telemetry/       # low-overhead sensor sampling
    D7.Games/           # detection, launcher inventory, profiles
    D7.Benchmark/       # PresentMon capture, parsing, confidence, A/B
    D7.Optimization/    # plans, reversible operations, policy engine
    D7.Rollback/        # transaction journal and recovery
    D7.Tools/           # acquisition, verification, repair, child-process host
    D7.Drivers/         # driver inventory/intelligence
    D7.Network/         # read-first network/latency analysis
    D7.Storage/         # storage health and game-location analysis
    D7.Streaming/       # OBS/NVENC/streaming state
    D7.Intelligence/    # signed/versioned rule catalogs + cache
    D7.Updater/         # staged update and health rollback
    D7.Diagnostics/     # safe diagnostics package
    D7.Native/          # native contracts/IPC boundary
  tests/
    D7.UnitTests/
    D7.IntegrationTests/
    D7.RegressionTests/
    D7.EndToEndTests/
  docs/
  build/
  installer/
```

## Core architectural rules

1. Single source of truth: no runtime function overrides, no source concatenation, no regex code rewriting.
2. UI thread is presentation only. Scans, downloads, benchmarks and tool invocations are asynchronous/cancellable.
3. External and native tools are isolated behind typed interfaces and bounded by timeout/cancellation.
4. Every persistent optimization is an atomic transaction with durable journal.
5. Every hardware feature exposes capability state: `Supported`, `ReadOnly`, `Unavailable`, `Experimental`.
6. No hard-coded global “best tweak”. Decisions require evidence and supported-hardware predicates.
7. Online catalogs are versioned, cached and verified; offline core remains usable.
8. Stable is a promotion of the exact tested RC artifact; no rebuild during promotion.

## Process model

Main process: `D7.Blackcore.exe`

Potential isolated workers:
- PresentMon capture host
- ETW trace worker
- high-risk experimental CPU/GPU worker
- updater/replacer

Workers communicate through a versioned local IPC contract (Named Pipes preferred). The main app owns worker lifetime and uses kill-on-parent-exit semantics/job objects where practical.

## Data model

Machine-wide data: `%ProgramData%\D7 BLACKCORE\`

Per-user UI/cache data: `%LocalAppData%\D7 BLACKCORE\`

Persistent categories:
- Config
- Profiles
- Measurements
- Transactions
- Tools
- IntelligenceCache
- Logs
- Diagnostics
- Updates

SQLite is preferred for histories/measurements/transactions; small immutable manifests may use JSON. All important writes are atomic and schema-versioned.

## UI architecture

MVVM. Arabic is the default resource culture. Main flow is one-click oriented with an Advanced mode for technical detail. All mixed Arabic/English unit strings must be explicitly RTL-tested.

## Performance budget

Gaming mode target behavior:
- no busy loops
- no recursive filesystem scans
- no high-frequency CIM/WMI polling
- telemetry sampling adaptive by state
- bounded allocations
- background I/O batched
- D7 CPU/disk/memory overhead self-monitored

Any reproducible frametime regression attributable to D7 is a release blocker.
