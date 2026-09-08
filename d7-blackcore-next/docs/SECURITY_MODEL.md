# D7 BLACKCORE Next — Security Model

## Trust boundaries

1. Main D7 process.
2. Local privileged operations.
3. External tools/native workers.
4. Online catalogs/update payloads.
5. User game/streaming processes.
6. Persistent state/transaction journal.

No boundary is implicitly trusted because it is local.

## Least privilege

- UI should not run permanently elevated if architecture can split privileged operations into a narrowly scoped helper.
- Privileged helper exposes only explicit typed operations, never arbitrary command execution.
- High-risk hardware experiments are isolated and disabled unless capability validation and explicit policy allow them.

## Forbidden automatic behavior

- Defender/Firewall disabling.
- Windows Update disabling.
- BCD/HPET/dynamic-tick hacks.
- RealTime priority.
- arbitrary service deletion.
- random registry tweak packs.
- blind RAM purge loops.
- anti-cheat manipulation or game-memory access.
- automatic BIOS flashing.
- unverified driver/tool downloads.

## Tool/download trust

For every external payload:
1. Resolve official source from a signed/versioned catalog or compiled trust anchor.
2. Enforce HTTPS.
3. Verify expected SHA-256.
4. Verify Authenticode publisher where applicable.
5. Stage in quarantine/temp path.
6. Move to executable tool path only after verification.
7. Delete on any integrity failure.

Mirrors and unsigned replacement URLs are forbidden by default.

## Update security

- Stable manifest and package are separate from development channels.
- Package hash is mandatory.
- Prefer signed manifest/package when signing infrastructure is available.
- Update is staged, verified, launched with health-check window, then committed.
- If health check fails, revert to previous executable set.
- Profiles/history are never overwritten by binary rollback.

## Transaction safety

All optimization writes use a durable transaction journal:
- capture original state first
- append operation before/after state atomically
- verify each operation
- mark transaction committed only after post-apply verification
- incomplete transaction is recovered before new changes are allowed

## Privacy

Diagnostics/telemetry must exclude:
- passwords
- cookies
- browser history
- personal files/documents/photos
- messages
- auth tokens/secrets

Allowed data:
- hardware identity/specs
- Windows/system performance state
- driver/tool versions
- D7 operation history
- benchmark/telemetry measurements
- local D7 error details

## Anti-cheat boundary

D7 may observe process identity, foreground state, OS-level telemetry and documented performance counters. It must not inject into game processes, read/write game memory, hook anti-cheat-sensitive code paths or attempt bypass behavior.

## Safe Mode

Safe Mode disables automatic optimization, external integrations and game-profile writes. It must remain available even if normal config/catalogs are corrupt and provide recovery to last committed stable transaction.
