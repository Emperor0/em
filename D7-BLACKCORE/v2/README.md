# D7 BLACKCORE 2.0

`2.0.0-dev.1` — clean-room kernel foundation on a dedicated branch.

This milestone is intentionally not a patched BLACKCORE 1.x build. It establishes the contracts that future Windows runtime, Tauri UI, Device, Money, Builder, Creator and browser-control layers must obey.

## Implemented in dev.1

- State Engine
- Event Bus
- Mission Engine + watchdog detection + quality gate
- Permission/Approval Engine
- Evidence Engine
- Capability Truth Registry
- Provider-agnostic Model Gateway
- Mode Registry and composition foundation
- Builder Studio planning/stack-ranking foundation
- Device basic snapshot adapter
- Money opportunity scoring + buyer-validation gate
- Modern one-screen desktop visual prototype
- Frozen Master Spec + architecture contract

## Verify

```bash
npm run verify
```

The production desktop shell target is Tauri 2 + Rust + React/TypeScript. The current static UI is a design/control-surface prototype while the Windows host/runtime is built behind the kernel contracts.
