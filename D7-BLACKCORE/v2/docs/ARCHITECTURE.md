# BLACKCORE 2.0 Architecture — dev.1

BLACKCORE 2.0 is an event-driven personal autonomous operating layer over Windows.

## Kernel contract

`Intent -> State/Context -> Plan -> Permission -> Action -> Observe -> Verify -> Evidence -> Memory/Learning`

No UI component owns business logic. The desktop UI is a projection of kernel state.

## Core services

- State Engine: single source of truth for current operational state.
- Event Bus: event-driven wake-up and mode composition.
- Mission Engine: goal, steps, watchdogs, quality gates and completion semantics.
- Permission Engine: risk/autonomy policy and approval firewall.
- Evidence Engine: proof attached to claims and mission completion.
- Capability Registry: truth about what BLACKCORE can actually do on this machine.
- Model Gateway: provider-agnostic model routing with privacy/quality/cost policy.
- Mode Registry: composed operational modes instead of isolated profile pages.

## Execution hierarchy

`API > CLI > DOM/Playwright > Windows UI Automation > Vision + Mouse/Keyboard`

## Desktop target

Production shell target: Tauri 2 + Rust host + React/TypeScript UI. Kernel interfaces remain transport-agnostic so the UI can be replaced without rewriting mission logic.

## Important architectural rule

BLACKCORE 1.x is a reference/learning artifact. 2.0 is not built as a patch stack on top of 1.x.
