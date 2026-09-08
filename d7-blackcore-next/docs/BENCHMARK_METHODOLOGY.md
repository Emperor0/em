# D7 BLACKCORE Next — Benchmark Methodology

## Frame source

D7 uses the official GameTechDev PresentMon console in an isolated process. Captures target a specific game PID and request v2 metrics with dropped frames excluded from the primary displayed-frame analysis.

## Primary frame-time signal

Priority:
1. `MsBetweenPresents`
2. `MsBetweenDisplayChange`
3. `DisplayedTime`

The selected column is recorded with every analysis so method changes remain auditable.

## FPS and low metrics

D7 does not average per-frame instantaneous FPS for the primary average. Average FPS is:

`frame_count * 1000 / sum(frame_times_ms)`

This follows time-domain frametime analysis practice used by tools such as CapFrameX.

`1% Low Average FPS` is defined as `1000 / average(slowest frame-time tail at the R-7 99th percentile threshold)`.

`0.1% Low Average FPS` uses the same definition at the 99.9th frame-time percentile threshold.

D7 records `MethodVersion` with every measurement. Changing percentile/tail definitions requires a new method version; measurements from incompatible method versions must not be silently compared.

## Percentiles

D7 currently uses the R-7 linear-interpolation quantile definition for frame-time percentiles (median, P95, P99, P99.9).

## Stutter metric

Stutter classification is deliberately separate from “slow frame”. The initial D7 method compares chronological frame time against a local moving average with a conservative 2.5x factor, inspired by established frametime analysis practice. The algorithm is versioned and must be validated against synthetic traces before it may influence automatic rollback decisions.

## A/B verdict

Primary ordering:
1. stability regressions
2. 1% low
3. P99 frame time
4. stutter behavior
5. average FPS

A stability regression forces rollback regardless of average FPS gain. Small differences fall into `INCONCLUSIVE` rather than being marketed as an improvement.

## Comparability

Before automatic A/B verdicts are enabled in production, a scenario fingerprint must match at least:
- game/process identity
- resolution
- graphics API when available
- graphics-settings fingerprint
- display mode
- refresh rate
- capture duration range
- D7 metric method version

A mismatch lowers confidence or blocks comparison.
