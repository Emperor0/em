# D7 BLACKCORE Next — Migration Plan

## Objective

Replace the PowerShell/PS2EXE product line with a clean native Windows desktop architecture while preserving only proven behaviors and historical measurements. Development occurs exclusively on `blackcore-next`; existing stable artifacts/manifests remain untouched until RC promotion.

## Phase 0 — Discovery and evidence capture

- Inventory repository history and old D7 artifacts.
- Record known failures as regression cases.
- Freeze legacy PowerShell feature growth.
- Capture target machine baseline and tool inventory.
- Create architecture/security/dependency/test/feature/release documents.

Exit: documentation complete and reviewed against legacy evidence.

## Phase 1 — Foundation

Deliver internally:
- clean .NET solution
- Arabic RTL WPF shell
- dependency injection/composition root
- structured JSONL logging
- global exception handling
- single-instance mutex
- application directories + permissions model
- safe mode boot path
- bootstrap status pipeline
- no production tweak writes yet

Exit: application repeatedly starts/exits without orphan processes or UI freeze.

## Phase 2 — Discovery + telemetry

- one-time hardware/OS inventory
- capability matrix
- GPU telemetry via NVIDIA-supported interfaces when available
- low-overhead CPU/RAM/storage/network telemetry
- self-monitoring of D7 overhead
- tool inventory by executable + install metadata, never “installed=true” without a usable path when execution is required

Exit: target machine scan and telemetry are stable on Windows 10 build 19045.

## Phase 3 — Game + streaming context

- process/foreground detection
- launcher inventories (Steam/Battle.net/EA/Epic/Xbox where feasible)
- negative classifiers for OBS/TikTok/NVIDIA/dev/agent/system processes
- hysteresis for alt-tab/overlays
- profile persistence
- OBS/streaming context

Exit: 007 First Light and Call of Duty can be detected without reintroducing historical false positives.

## Phase 4 — Measurement core

- official PresentMon acquisition/verification
- isolated capture process
- typed CSV parser
- Avg FPS, 1%, 0.1%, frametime percentiles, stutters and supported CPU/GPU metrics
- scenario fingerprint
- confidence score
- measurement persistence

Exit: capture -> parse -> save -> Arabic result works end-to-end.

## Phase 5 — Transaction + A/B engine

- durable transaction journal
- reversible operation primitives
- interrupted-session recovery
- A/B experiment orchestration
- KEEP / ROLLBACK / INCONCLUSIVE verdict based primarily on consistency/stability

Exit: simulated failure at every transaction step returns machine state to baseline.

## Phase 6 — Safe optimization catalog

Start only with low-risk reversible operations:
- Game Mode (correctly detected)
- Game DVR background capture policy where applicable
- temporary power plan activation
- process priority within Normal/AboveNormal bounds
- background-relief priority changes

Every measurable operation is evaluated through A/B where meaningful.

Exit: no BCD/HPET/Defender/pagefile/Realtime/anti-cheat or unvalidated low-level changes.

## Phase 7 — Intelligence modules

- Startup Surgeon
- Driver Brain
- Network/Latency Lab
- Storage Lab
- Streaming Guardian
- Thermal Lab
- NVIDIA per-game profiles (only with documented backup/import path)

Exit: each module is capability-gated and explains evidence.

## Phase 8 — Advanced hardware labs

Only after measurement/rollback/stability are proven:
- CPU Lab
- GPU Lab
- RAM Advisor

High-risk operations require explicit capability support, small steps, stability gates and rollback. BIOS remains advisory/manual.

## Phase 9 — Installer/updater/productization

- single installer with install/repair/upgrade/uninstall
- profiles/history preserved across update
- online-only update channel for user-facing releases
- staged verified update with health rollback
- diagnostics bundle

## Phase 10 — QA and release

Internal progression only:
`dev -> alpha -> beta -> rc`

No user-facing artifact until scope is feature-complete. RC undergoes soak tests and real-hardware acceptance. The exact tested RC artifact is promoted to stable without rebuild.

## Migration of legacy data

Legacy learned-game/profile data may be imported through an explicit, versioned one-time migration. Raw legacy scripts are never executed by the new product merely to migrate settings. Importers must validate paths/process names and reject historical false-game entries.
