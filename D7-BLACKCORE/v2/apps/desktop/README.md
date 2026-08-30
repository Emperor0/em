# D7 BLACKCORE Native Desktop — dev.4

This is the first real Tauri 2 / Rust / React Windows shell for BLACKCORE 2.0.

Implemented in this milestone:
- Native desktop window; no Chrome or HTML-file launcher.
- Rust Device Twin (CPU/RAM/processes/disks).
- NVIDIA telemetry via `nvidia-smi` when present.
- Read-only bridge to the existing D7 Performance Governor status file.
- Local OpenCode `/global/health` probe without reading credentials.
- Native emergency automation pause state.
- Capability Truth surfaced from real runtime probes.

Still intentionally UNTESTED/PARTIAL until later milestones:
- Windows mutation/control adapters.
- Authenticated Chrome operator.
- Full Mission/Agent transport from the kernel into the Rust runtime.
- Builder Studio IDE surface.

No destructive Windows changes exist in this milestone.
