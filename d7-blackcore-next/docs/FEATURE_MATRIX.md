# D7 BLACKCORE Next — Feature Matrix

Status values: `PLANNED`, `INTERNAL`, `TESTED`, `RC_READY`, `STABLE_READY`.

Nothing in this development matrix is user-facing stable until the release gates pass. `TESTED` means the current internal implementation has automated coverage; it does not replace Windows 10 real-hardware acceptance.

| Feature | Scope | Required state before RC | Current development state |
|---|---|---|---|
| Arabic RTL shell | Dashboard/navigation/dialogs/tooltips/errors | TESTED | INTERNAL |
| Bootstrap | OS/permissions/storage/network/catalog/tool readiness | TESTED | INTERNAL |
| Single instance | Activate/block duplicate | TESTED | INTERNAL |
| Global error handling | no unhandled UI crash | TESTED | INTERNAL |
| Hardware Discovery | CPU/GPU/RAM/board/BIOS/storage/Windows/drivers/startup | TESTED | INTERNAL |
| Telemetry | low-overhead CPU/GPU/RAM/thermal + self-overhead | TESTED | INTERNAL |
| Capability Matrix | Supported/ReadOnly/Unavailable/Experimental | TESTED | INTERNAL |
| Game Detection | launchers/processes/negative classifier/hysteresis | TESTED | TESTED |
| Game Profiles | per-game measurement/settings history | TESTED | INTERNAL |
| OBS Guardian | streaming context and protection | TESTED | PLANNED |
| Tool Acquisition | official source/hash/signature/install/repair | TESTED | INTERNAL |
| PresentMon Frame Lab | acquire/capture/parse/persist/display | TESTED | INTERNAL |
| Benchmark Confidence | capture coverage/sample quality + confidence reasons | TESTED | TESTED |
| Scenario Fingerprint | game/settings/display comparability gate | TESTED | PLANNED |
| A/B Engine | comparable sessions + KEEP/ROLLBACK/INCONCLUSIVE | TESTED | TESTED |
| Transaction Journal | atomic operations + restart recovery | TESTED | TESTED |
| Safe optimization catalog | only reversible low-risk actions | TESTED | INTERNAL |
| Startup Surgeon | classify/disable/restore | TESTED | PLANNED |
| Driver Brain | keep/update/rollback/clean-install decision support | TESTED | PLANNED |
| Network/Latency Lab | read-first adapter/ETW analysis | TESTED | PLANNED |
| Storage Lab | health/free space/game location | TESTED | PLANNED |
| Thermal Lab | safe Fan Control integration/readiness | TESTED | PLANNED |
| NVIDIA per-game profiles | documented backup/import only | TESTED | PLANNED |
| CPU Lab | monitor/recommend/validated auto-tune where supported | TESTED | PLANNED |
| GPU Lab | monitor/recommend/stability-gated tuning | TESTED | PLANNED |
| RAM Advisor | read/analyze/BIOS recommendation/validation | TESTED | PLANNED |
| Learning Engine | local history/rules/per-game adaptation | TESTED | TESTED |
| Online Intelligence | versioned verified catalogs + cache | TESTED | PLANNED |
| Update staging | validate/download/hash/safe-extract/pending state | TESTED | INTERNAL |
| Update activation/health rollback | exact staged payload -> restart -> health gate -> rollback | TESTED | PLANNED |
| Safe Mode | recovery without optimizations/integrations | TESTED | INTERNAL |
| Diagnostics | privacy-safe report package | TESTED | TESTED |
| Installer | install/repair/upgrade/uninstall preserve data | TESTED | PLANNED |

## Core flow requirement

RC is forbidden until this works end-to-end:

`Install -> Launch -> Bootstrap -> Scan -> Detect Game -> Measure -> Diagnose -> Backup -> Optimize -> Verify -> Measure Again -> Compare -> Keep/Rollback -> Save Profile -> Restart App`

## Advanced-lab release rule

CPU/GPU/RAM low-level features are not considered complete because a button exists. A lab is complete only when its supported path implements detection, analysis, backup, apply where safe, verification, benchmark/stability validation and rollback. Unsupported hardware must show a truthful capability state rather than a fake implementation.

## Release honesty rule

No `INTERNAL` or `TESTED` item may be called RC-ready until its required integration, Arabic UX, failure handling and Windows 10 build 19045 acceptance are complete. Update staging is deliberately separate from update activation so a secure downloader is never mislabeled as a complete self-updater.
