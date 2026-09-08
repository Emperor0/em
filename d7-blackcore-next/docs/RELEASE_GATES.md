# D7 BLACKCORE Next — Release Gates

## Channel separation

- `dev`: continuous internal builds only.
- `alpha`: internal feature integration.
- `beta`: feature-complete candidate testing.
- `rc`: exact artifact intended for promotion.
- `stable`: production only.

Development workflows must never write `d7-gaming-engine/stable/latest.json`, production packages or the production updater channel.

## Mandatory gates

Before an artifact may become RC:

- BUILD: PASS
- UNIT TESTS: PASS
- INTEGRATION TESTS: PASS
- REGRESSION TESTS: PASS
- END-TO-END CORE FLOW: PASS
- ARABIC UI: PASS
- RTL/MIXED-UNIT QA: PASS
- STARTUP/SINGLE INSTANCE: PASS
- HARDWARE DISCOVERY: PASS
- TOOL ACQUISITION/REPAIR: PASS
- PRESENTMON CAPTURE/PARSER: PASS
- GAME DETECTION: PASS
- A/B ENGINE: PASS
- TRANSACTION/ROLLBACK: PASS
- UPDATER STAGING/ROLLBACK: PASS
- SAFE MODE: PASS
- OFFLINE BEHAVIOR: PASS
- DIAGNOSTICS PRIVACY: PASS
- INSTALLER/REPAIR/UPGRADE/UNINSTALL: PASS
- UI RESPONSIVENESS: PASS
- ORPHAN CHILD PROCESS TEST: PASS
- ZERO KNOWN CRITICAL BUGS: PASS
- ZERO KNOWN HIGH BUGS: PASS

## RC soak gate

The RC artifact must survive repeated start/close, sleep/wake, internet loss/return, missing/corrupt tool, hash failure, game close/crash, PresentMon crash, permission failures, duplicate launch, high RAM pressure, OBS+game, interrupted transaction and updater failure.

## Real hardware acceptance gate

On the target Windows 10 Pro build 19045 PC:
- app launches self-contained
- Arabic RTL renders correctly
- Ryzen 5 3600 / RTX 2060 SUPER telemetry is credible
- 007 First Light and Call of Duty detection works without false-learned stream/dev processes
- PresentMon produces a valid frame capture
- D7 does not cause reproducible frametime regression
- transaction restore works
- diagnostics package is generated

The acceptance test may discover environment-specific problems, but stable promotion is blocked until they are corrected and required suites rerun.

## Exact-artifact promotion

Stable promotion MUST use the exact RC artifact that passed tests. Record:
- artifact SHA-256
- source commit SHA
- CI run ID
- test report identity
- acceptance report identity

No rebuild is allowed between RC acceptance and stable publication.

## Release blockers

Any crash, unhandled exception, frozen core UI, broken primary action, wrong benchmark math, rollback failure, updater failure, profile/history data loss, unexplained manual dependency, English primary UI, orphan worker, known regression or measurable D7-induced gameplay degradation blocks stable.
