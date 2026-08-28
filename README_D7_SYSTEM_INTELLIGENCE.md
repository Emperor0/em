# D7 System Intelligence

Windows performance, diagnostics, launcher discovery, safe game-config adapters, fan control, update management and repair tooling in one native desktop application.

## Current implemented modules
- Live CPU/GPU/RAM/VRAM/temperature/fan telemetry via LibreHardwareMonitor.
- Safe fan control only when a writable hardware control is exposed; firmware/default control is restored on exit.
- Launcher/game discovery for Steam, Epic, Xbox folders, Ubisoft registry installs and registry-backed EA/Battle.net/Rockstar/GOG/Amazon installs.
- COD beta schema-aware config adapter using verified key signatures observed in the supplied KALLEX binary. It backs up, writes to a temporary file, validates, then atomically replaces the original.
- Quick diagnostics for thermal pressure, RAM/VRAM pressure, WHEA, NVIDIA driver events, disk errors, free disk space and startup load.
- Winget update scan/update and non-destructive Windows health scans (DISM ScanHealth + SFC verifyonly).
- One-file self-contained application plus Inno Setup installer built by GitHub Actions.

## Safety boundaries
D7 does not disable Defender/firewall, patch game executables, bypass anti-cheat, force unsupported fan controllers, or apply undocumented overclocks. Any config mutation is backup-first and known-key-only.
