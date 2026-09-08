# D7 BLACKCORE Next — Test Matrix

## Test layers

### Unit
- decision scoring
- benchmark statistics
- 1% / 0.1% low calculations
- percentile calculations
- confidence scoring
- capability predicates
- transaction serialization
- hash verification
- version/catalog parsing
- Arabic resource lookup

### Integration
- filesystem layout and atomic writes
- SQLite schema/migrations
- Windows registry read/write through reversible operation wrappers
- power-plan capture/apply/restore
- child-process supervision
- PresentMon CLI contract and CSV parser
- tool acquisition with local test server/cache
- game process classifier
- updater staging/verification

### Regression
Permanent historical tests:
- DisplayName/source-corruption class of bug cannot occur because production build does not rewrite source text.
- duplicate instance blocked.
- long scan never blocks UI dispatcher.
- missing PresentMon self-heals/degrades safely.
- offline startup works.
- game closes during capture -> controlled cancellation.
- corrupt catalog -> cached verified catalog.
- tool hash mismatch -> delete/block.
- OBS/TikTok/MediaSDK/NVIDIA/D7Agent/Python/Node/PowerShell are not learned as games by default.
- tool metadata without executable path is not considered executable-ready.
- interrupted transaction recovers.
- child workers do not survive app exit.
- updates preserve profiles/measurements/history.

### End-to-end
1. Install -> launch -> first-run bootstrap -> scan -> ready state.
2. Detect game -> baseline -> diagnose -> reversible optimization -> remeasure -> verdict -> keep/rollback -> persist profile -> restart app.
3. Streaming path: OBS + game -> streaming score/telemetry -> no harmful game-only decision.
4. Safe Mode -> restore last stable state.
5. Diagnostics package excludes private data categories.

## Fault injection matrix

| Scenario | Expected result |
|---|---|
| No game running | UI remains usable; measurement explains no active supported game |
| Game exits mid-capture | capture canceled; worker cleaned; no crash |
| Internet disconnected | offline core continues; cached verified data used |
| Internet returns | bounded retry/backoff, no duplicate downloads |
| PresentMon missing | acquire/verify or controlled unavailable state |
| PresentMon corrupted | hash failure -> delete -> repair |
| Tool crashes | worker isolated; main app survives |
| Catalog JSON corrupt | reject; keep last verified cache |
| Access denied | controlled error; no half-applied state |
| D7 launched twice | second instance exits/activates first |
| D7 closed during apply | journal allows recovery next start |
| Windows restart during transaction | startup recovery before new optimization |
| Sleep/wake | telemetry and workers recover cleanly |
| OBS + game | streaming-aware policy preserved |
| High RAM pressure | no blind purge; diagnosis only unless proven action exists |
| GPU/driver query fails | capability becomes unavailable; UI survives |
| Installer repair | binaries repaired; profiles/history preserved |

## Windows 10 build 19045 acceptance

The following must be validated on the real target PC, not only CI:
- self-contained app launch
- WPF RTL rendering
- hardware sensors
- NVIDIA telemetry
- PresentMon capture
- game detection for 007 First Light and Call of Duty
- transaction apply/restore
- updater stage/health check
- no orphan processes
- D7 overhead during gameplay

## Performance acceptance

During gaming, D7 must remain low-overhead and must not introduce reproducible frametime spikes. Any statistically repeatable regression caused by D7 is a release blocker regardless of functional test status.

## Release rule

A build can be successful while the product is not release-ready. Stable requires all mandatory gates plus RC soak and real-hardware acceptance.
