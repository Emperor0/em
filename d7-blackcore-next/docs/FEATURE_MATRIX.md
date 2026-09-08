# D7 BLACKCORE Next — Feature Matrix

Status values: `PLANNED`, `INTERNAL`, `TESTED`, `RC_READY`, `STABLE_READY`.

Nothing in this development matrix is user-facing stable until the release gates pass.

| Feature | Scope | Required state before RC | Current development state |
|---|---|---|---|
| Arabic RTL shell | Dashboard/navigation/dialogs/tooltips/errors | TESTED | PLANNED |
| Bootstrap | OS/permissions/storage/network/catalog/tool readiness | TESTED | PLANNED |
| Single instance | Activate/block duplicate | TESTED | PLANNED |
| Global error handling | no unhandled UI crash | TESTED | PLANNED |
| Hardware Discovery | CPU/GPU/RAM/board/BIOS/storage/Windows/drivers/startup | TESTED | PLANNED |
| Telemetry | low-overhead CPU/GPU/RAM/thermal + self-overhead | TESTED | PLANNED |
| Capability Matrix | Supported/ReadOnly/Unavailable/Experimental | TESTED | PLANNED |
| Game Detection | launchers/processes/negative classifier/hysteresis | TESTED | PLANNED |
| Game Profiles | per-game measurement/settings history | TESTED | PLANNED |
| OBS Guardian | streaming context and protection | TESTED | PLANNED |
| Tool Acquisition | official source/hash/signature/install/repair | TESTED | PLANNED |
| PresentMon Frame Lab | acquire/capture/parse/persist/display | TESTED | PLANNED |
| Benchmark Confidence | scenario fingerprint + confidence reasons | TESTED | PLANNED |
| A/B Engine | comparable sessions + KEEP/ROLLBACK/INCONCLUSIVE | TESTED | PLANNED |
| Transaction Journal | atomic operations + restart recovery | TESTED | PLANNED |
| Safe optimization catalog | only reversible low-risk actions | TESTED | PLANNED |
| Startup Surgeon | classify/disable/restore | TESTED | PLANNED |
| Driver Brain | keep/update/rollback/clean-install decision support | TESTED | PLANNED |
| Network/Latency Lab | read-first adapter/ETW analysis | TESTED | PLANNED |
| Storage Lab | health/free space/game location | TESTED | PLANNED |
| Thermal Lab | safe Fan Control integration/readiness | TESTED | PLANNED |
| NVIDIA per-game profiles | documented backup/import only | TESTED | PLANNED |
| CPU Lab | monitor/recommend/validated auto-tune where supported | TESTED | PLANNED |
| GPU Lab | monitor/recommend/stability-gated tuning | TESTED | PLANNED |
| RAM Advisor | read/analyze/BIOS recommendation/validation | TESTED | PLANNED |
| Learning Engine | local history/rules/per-game adaptation | TESTED | PLANNED |
| Online Intelligence | versioned verified catalogs + cache | TESTED | PLANNED |
| Updater | staged verified health rollback | TESTED | PLANNED |
| Safe Mode | recovery without optimizations/integrations | TESTED | PLANNED |
| Diagnostics | privacy-safe report package | TESTED | PLANNED |
| Installer | install/repair/upgrade/uninstall preserve data | TESTED | PLANNED |

## Core flow requirement

RC is forbidden until this works end-to-end:

`Install -> Launch -> Bootstrap -> Scan -> Detect Game -> Measure -> Diagnose -> Backup -> Optimize -> Verify -> Measure Again -> Compare -> Keep/Rollback -> Save Profile -> Restart App`

## Advanced-lab release rule

CPU/GPU/RAM low-level features are not considered complete because a button exists. A lab is complete only when its supported path implements detection, analysis, backup, apply where safe, verification, benchmark/stability validation and rollback. Unsupported hardware must show a truthful capability state rather than a fake implementation.
