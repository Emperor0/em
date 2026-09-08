# D7 BLACKCORE Next — Dependency Matrix

## Policy

External components are allowed only when their source, version, license and integration boundary are known. The application must not ask the user to install a tool manually when D7 can safely acquire and verify it itself. Missing optional tools must degrade capability, not crash the application.

| Dependency | Purpose | Integration | Acquisition | Verification | Required for core? | Notes |
|---|---|---|---|---|---|---|
| .NET Windows Desktop runtime | Main application | self-contained publish | bundled by publish | build manifest/hash | Yes | No machine-wide runtime dependency. Windows 10 build 19045 real-hardware gate required. |
| PresentMon (GameTechDev) | frame capture / A-B | verified CLI, isolated process | official GitHub release/catalog | SHA-256 + source metadata; signature if available | Measurement core | Never use mirror. Capture process must be cancellable and killed on app exit. |
| LibreHardwareMonitorLib | sensor telemetry candidate | embedded library | NuGet/source package subject to license review | package lock + hash | No; preferred telemetry backend | Use only where sensor support/performance passes target hardware tests. |
| NVIDIA NVML / nvidia-smi | NVIDIA telemetry | official native API / CLI fallback | NVIDIA driver installation | executable/library publisher + path | No | Read-only first; no OC writes. |
| System Informer | deep diagnosis | launch/reference or documented interface only | official project release if needed | hash/signature | Optional | Do not make core dependent on it. |
| Fan Control | thermal profile assistance | launch/read-only until documented contract verified | official release if needed | hash/signature | Optional | No undocumented config rewriting. |
| NVIDIA Profile Inspector | per-game NVIDIA profile experiments | documented CLI only | official repository/release | hash + backup/export verification | Optional | Never mutate global profile blindly. |
| ZenTimings | RAM inspection | launch/read-only/reference | official source | hash/signature where available | Optional | RAM tuning remains BIOS advisor until safe interface proven. |
| Windows Performance Toolkit / xperf/WPR | DPC/ISR/ETW latency analysis | official Microsoft tools | Microsoft installer/Windows SDK path | Microsoft signature | Optional advanced | Missing toolkit must not block normal D7 operation. |
| Autoruns/Autorunsc | startup inventory reference/CLI | official Sysinternals CLI where licensing permits | Microsoft Sysinternals | Microsoft signature | Optional | Startup Surgeon should also use native Windows inventory so it is not hard-dependent. |
| winget | app/update inventory helper | CLI | Windows-provided/App Installer | Microsoft signature | Optional | Never blind-update drivers. |
| Ryzen Master | supported AMD tuning/telemetry research | capability/reference | AMD official | AMD signature | Optional | Do not depend on undocumented automation. |
| ryzen_smu / ZenMaster | low-level Ryzen research | experimental isolated native worker only | source/release subject to license/security review | source/release hash | No | HIGH/EXPERIMENTAL; never auto-enable in stable without dedicated validation. |
| OBS | streaming context | process + local stats/log/API where documented | user installation | executable path/signature | Optional | Preserve OBS from background relief. |

## Tool acquisition service contract

`IToolAcquisitionService` must support:
- CheckInstalledAsync
- ResolveOfficialSourceAsync
- DownloadAsync
- VerifyHashAsync
- VerifySignatureAsync
- InstallOrStageAsync
- VerifyInstallationAsync
- RepairAsync
- RollbackInstallationAsync

Every acquisition record stores: tool id, vendor, version, official source, expected/actual SHA-256, signature state, installation path, result and timestamp.

## Failure behavior

- Hash mismatch: delete payload immediately; mark tool unavailable; no execution.
- Signature/source mismatch: block execution.
- Internet unavailable: use verified cached artifact if valid; otherwise mark dependent feature unavailable with Arabic explanation.
- Tool removed/corrupted after installation: self-heal through re-verification and repair.
- Tool process crash: isolate; surface controlled Arabic error; main app remains alive.
