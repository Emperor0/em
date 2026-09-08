# D7 LIVE Architecture

D7 LIVE is intentionally split into stable application logic and platform/media adapters.

## Core pipeline

Capture providers -> scene graph -> GPU composition -> final frame -> encoder / recorder / virtual output.

Audio providers -> filters / routing -> stream mix / recording mix / monitor mix / virtual mix.

LIVE provider -> normalizer -> event bus -> rules -> alert queue / actions.

## Boundary rules

- TikTok-specific logic lives behind a provider interface.
- Virtual camera/audio live behind explicit capability boundaries.
- UI calls stable Tauri commands, not device APIs directly.
- Config writes are atomic and retain a last-known-good copy.
- Update packages must be signed and verified before execution.

## Media implementation target

The preferred production route is libobs for mature capture/audio/scene primitives with a Rust adapter layer. Windows Graphics Capture/D3D11 can be used for targeted native capture paths. NVENC is preferred on NVIDIA systems. FFmpeg is used where it adds mature codec/container/remux functionality rather than recreating it.

## Driver constraints

A distributable custom virtual audio device and some virtual camera strategies require Windows signing and packaging work on a Windows development/signing environment. The UI and capability API must report truthful states: Installed, Running, Unavailable, Repair Required.
