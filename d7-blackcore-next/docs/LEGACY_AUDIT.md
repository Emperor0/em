# D7 BLACKCORE Next — Legacy Audit

Status: development-only. This document must never trigger stable publication.

## Scope reviewed

The legacy review covers the current `Emperor0/em` repository history (`d7-gaming-engine`, `D7-BLACKCORE`, old GitHub Actions workflows, hotfixes and release packaging) plus available historical D7 artifacts: D7 Performance Governor, D7 Gaming Engine deep/A-B tests, BLACKSCAN, detector guards, installers/debug launchers and D7 System Intelligence traces.

## Findings

| Project / component | Useful idea | Good implementation / behavior | Known defect / technical debt | Decision |
|---|---|---|---|---|
| D7 Performance Governor | State machine for IDLE/GAME/STREAM/STREAM_GAME | Restores original power plan; streaming-aware process priorities; GPU/CPU/RAM telemetry; conservative safety policy | PowerShell long-running worker, Scheduled Task lifecycle, polling/CIM overhead, manual install, duplicated revisions | REWRITE behavior in C# services; keep state-machine concept |
| Governor detector v1.2–v1.5 | Learned-game detection + hold/hysteresis | Foreground/process path + confidence + learned games; later guard excludes OBS/TikTok/NVIDIA/dev tools/D7Agent/Python/Node/PowerShell | False positives existed before guard; regex lists are brittle and grew by hotfix | REFACTOR into evidence-scored GameDetectionService with negative classifiers and launcher metadata |
| D7 Gaming Engine deep test v0.4 | PresentMon-based real measurement | Official PresentMon 2.5.1 CLI, timed capture, JSON output | User-facing/manual flow and PowerShell process handling | REWRITE as isolated PresentMon worker with typed result model |
| D7 A/B v0.5.2 | Alternating baseline/optimized phases + restoration | 29/29 apply operations and full baseline restore showed transaction concept is viable | Runtime `Argument types do not match` after test; typed collection problems | KEEP experiment design; REWRITE engine in strongly typed C# with transaction journal |
| D7 Gaming Engine v1/v2 | Online SHA-256 updater, learned profiles, BLACKSCAN, measurement core | Measure-first policy; backup/rollback; single-instance guard added later | `finally` runtime issue, `.DisplayName` corruption, regex replacement source corruption, UI blocking, recursive Program Files scan, repeated hotfix releases | REMOVE source-patching architecture; REWRITE all production logic |
| BLACKSCAN | Machine-specific hardware/system inventory | Captured Ryzen 5 3600, B450 AORUS ELITE, RTX 2060 SUPER, 16 GB DDR4 and installed tool/app inventory | Earlier fixed-path tool detector returned zero tools; Game Mode interpretation was too shallow | REWRITE scanner; preserve one-time CIM/WMI inventory only where appropriate, no high-frequency polling |
| D7 BLACKCORE v2.1.x | Runtime safety, measurement baseline, tool inventory | Mutex, error dialogs, PresentMon analysis, read-only latency/network audit | Monolithic PowerShell + PS2EXE; build-time composition; synchronous operations | REWRITE into modular .NET solution |
| D7 System Intelligence 0.8 | Capability gating and installed-shell health checks | Supported/Read-only/Unavailable model, updater/installer/rollback concepts | Previous CI reliability issues and separate product overlap | REUSE capability-state model; REWRITE implementation |
| Legacy installers/debug BATs | Self-elevation, visible diagnostic mode | Helpful missing-file and fatal-error visibility during development | ExecutionPolicy bypass, BAT/PS1 coupling, user-as-debugger flow | KEEP ideas only; production installer becomes signed Windows installer; diagnostics integrated in-app |
| Old workflows | Build automation and package hashing | Some successful PS2EXE packaging and stable manifest updates | Many failed runs; stable publication coupled too closely to build; encoding and PowerShell parsing failures | REMOVE from new pipeline; build/test/RC/promotion must be separate |

## Permanent regression cases derived from history

1. `Regression_DuplicateInstance`
2. `Regression_LongScanUIFreeze`
3. `Regression_MissingPresentMon`
4. `Regression_InternetDisconnected`
5. `Regression_GameClosesDuringCapture`
6. `Regression_CorruptManifest`
7. `Regression_ToolHashMismatch`
8. `Regression_FalseGame_OBS_TikTok_Agent`
9. `Regression_ToolDetectedWithoutExecutablePath`
10. `Regression_TransactionInterrupted`
11. `Regression_OrphanChildProcess`
12. `Regression_ArabicRtlMixedUnits`
13. `Regression_UpdateDoesNotDeleteProfiles`
14. `Regression_StableNotTouchedByDev`

## Safety principles carried forward

- No automatic Defender/Firewall/Windows Update disabling.
- No HPET/BCD/dynamic-tick packs.
- No blind RAM purge loops.
- No RealTime priority.
- No automatic BIOS flashing.
- No CPU/GPU voltage/clock writes before measurement, capability validation, stability gate and rollback.
- No random TCP/Nagle/MTU registry packs.
- No anti-cheat bypass or game-memory manipulation.
- Pagefile is preserved unless a separate evidence-backed decision explicitly requires otherwise.

## Current target profile

Primary acceptance hardware: Windows 10 Pro build 19045 x64, Ryzen 5 3600 (6C/12T), Gigabyte B450 AORUS ELITE BIOS F68a, 16 GB TEAMGROUP DDR4-3200, RTX 2060 SUPER 8 GB, Realtek Gaming GbE, NVMe + SATA storage.

## Reuse policy

Legacy code is not copied into production merely because it works. Each behavior is classified KEEP / REFACTOR / REWRITE / REMOVE. PowerShell may remain support-only for development/diagnostics, never the main runtime or UI. Production source must be a single source of truth and compile directly without regex/text source patching.
