# D7KT Feature Value Audit — 2026

هذا الملف هو Gate إلزامي قبل اعتبار أي Feature جزءًا من المنتج. وجود صفحة أو زر لا يعني نجاح الميزة.

## قرار الميزة
- KEEP: لها قيمة قوية ومثبتة وتعمل فعليًا.
- UPGRADE: لها قيمة حقيقية لكن التنفيذ الحالي أضعف من المنافسين أو ناقص.
- MERGE: فائدتها موجودة لكن لا تستحق صفحة/منتج مستقل؛ تدمج داخل مركز أقوى.
- REMOVE: لا تقدم قيمة عملية واضحة أو مجرد UI/Vanity feature.

## Definition of Done لكل Feature
1. Use case واضح للمستخدم.
2. Implementation فعلي، ليس Text/Button فقط.
3. Supported / Read-only / Unavailable واضحة للهاردوير المعتمد على الدعم.
4. قياس قبل/بعد إذا كانت الميزة تدعي تحسين الأداء.
5. Backup/Restore لأي تعديل مستمر أو حساس.
6. لا ادعاء أرقام لا يمكن قياسها برمجيًا.
7. مقارنة بمنافس عالمي مباشر وتحديد سبب وجود D7KT بدل تشغيل المنافس فقط.
8. Build + Installer + Runtime test على الجهاز المستهدف قبل وصفها Production-ready.

## Feature Audit Queue

| Priority | Feature | Current decision | Benchmark / competitors | Gate before KEEP |
|---|---|---|---|---|
| P0 | Self Update | KEEP / HARDEN | Battle.net / Steam-style updater | progress visible, SHA-256, restart, failure reason, later signed manifest + rollback |
| P0 | RGB Studio | UPGRADE IN PROGRESS | SignalRGB / iCUE / Razer Chroma / OpenRGB | per-device, modes, brightness, scenes, game/runtime intelligence, zones/per-LED only when backend supports |
| P0 | Input Intelligence | UPGRADE IN PROGRESS | G HUB / Razer Synapse / SteelSeries GG | real polling distribution, jitter/stalls, Windows path backup/restore, NKRO, controller drift/range, vendor adapters only when real |
| P0 | Shadow Capture | UPGRADE | NVIDIA App / Medal / SteelSeries Moments / OBS Replay | duration presets/custom, folder/game folders, hotkeys, resource budget, measurable FPS/frametime impact, no duplicate recorder |
| P0 | Mission Engine | UPGRADE | Armoury Crate profiles / Process Lasso-style automation | every mission must list actual applied actions + verification + restore; remove no-op steps |
| P0 | Diagnostics + Action Center | UPGRADE | Windows Security/SupportAssist/HWiNFO-style diagnostics | every finding actionable/read-only/unavailable; repair verification after action |
| P0 | Stutter Black Box | UPGRADE | CapFrameX / PresentMon / FrameView | reliable frametime source, P1/P0.1/P95/P99, event correlation, session regression |
| P0 | Benchmark Lab | UPGRADE | CapFrameX / OCCT workflow | baseline-change-retest automatic comparison + accept/reject + rollback |
| P1 | Stream Director | UPGRADE | OBS Stats / NVIDIA Broadcast ecosystem | render/encode lag, dropped frames, encoder load, network pressure, dual-goal FPS+stream stability |
| P1 | D7 HUD | UPGRADE | RTSS / NVIDIA overlay / Xbox Game Bar | minimal customizable OSD, reliable frametime/FPS source, no game injection when unsafe |
| P1 | Network Intelligence | UPGRADE | PingPlotter / Bufferbloat tests / cFosSpeed concepts | gateway/internet split, jitter/loss, process bandwidth, before/after tuning, rollback |
| P1 | Display Intelligence | UPGRADE | NVIDIA Control Panel / Windows Advanced Display / Monitorian | Hz/VRR/HDR/bit depth/range/scaling/DDC, profiles, verify applied state |
| P1 | Audio Intelligence | REVIEW | SteelSeries Sonar / Voicemeeter / Windows audio | real endpoint/routing/sample rate/DPC diagnostics; remove controls that only open another app |
| P1 | Driver Safety | REVIEW | NVIDIA App / DDU / Snappy Driver concepts | official source comparison, backup/restore, A/B verification; no blind newest-driver logic |
| P1 | Storage Center | REVIEW | CrystalDiskInfo / smartctl / Windows Optimize Drives | SMART/reliability/temp/free/I/O + action only when safe; no duplicate basic info |
| P1 | Crash Investigator | REVIEW | Reliability Monitor / Event Viewer / WhoCrashed | correlate WHEA/GPU/storage/app crash timeline and give evidence-based next action |
| P1 | Smart Fans | CONDITIONAL KEEP | FanControl / BIOS fan curves | writable-channel proof, hysteresis, emergency rule, crash restore; Read-only otherwise |
| P1 | Restore Vault | KEEP / HARDEN | System Restore / app transaction logs | every persistent D7 action registered, diff visible, one-click verified restore |
| P2 | Startup Manager | MERGE CANDIDATE | Windows Task Manager / Autoruns | value must exceed enable/disable list via impact evidence and safe recommendations |
| P2 | Background Apps | MERGE CANDIDATE | Task Manager / Process Lasso | session-aware governor with measured benefit; remove generic process killing |
| P2 | Smart Removal | REVIEW | Revo Uninstaller / BCUninstaller | only keep if leftover discovery is materially better and preview-first |
| P2 | Clip Library | MERGE INTO CAPTURE | Medal / Moments libraries | game/date metadata, rename/move/delete/trim, storage policy; no standalone vanity page |
| P2 | Auto Scene | MERGE INTO MISSIONS | game profile auto-switchers | should be policy layer, not separate product; stable detection + debounce + restore |
| P2 | Performance Contract | MERGE INTO MISSIONS/BENCHMARK | adaptive performance targets | must enforce measurable target and report pass/fail; otherwise remove separate page |
| P2 | COD Adapter | CONDITIONAL KEEP | game config tools | only documented/verified keys, backup, game-closed guard, before/after result |
| P2 | Launcher Scanner | KEEP AS INFRA | Playnite / launchers | broad reliable discovery; not user-facing feature by itself |
| P3 | Raw peripheral inventory | MERGE | Device Manager | infrastructure only; surface useful device pages, not raw lists |
| P3 | Raw driver inventory | MERGE | Device Manager | infrastructure only; user sees only actionable important drivers |

## Current hard rules
- لا Feature تبقى فقط لأنها أخذت وقت بناء.
- إذا برنامج عالمي يقدمها أفضل ولا يوجد عند D7KT integration/intelligence advantage واضح: ندمج/نزيل أو نستفيد من backend بدل تقليد ناقص.
- Vendor-specific hardware writes تحتاج protocol/SDK موثق أو adapter مختبر؛ لا fake DPI/RGB/Fan/firmware controls.
- Security, Defender, Firewall, anti-cheat protections لا يتم تعطيلها لمكسب FPS وهمي.
- كل claims عن latency/FPS/temperature/network يجب ربطها بقياس فعلي أو تصنيفها Estimate/Unavailable.
- UI count ليس KPI. عدد الميزات الأقل مع execution أقوى أفضل من عشرات الصفحات.

## Audit order
RGB → Input → Shadow Capture → Missions → Diagnostics/Action Center → Stutter/Benchmark → Stream/HUD → Network → Display → Audio → Drivers → Storage/Crash → Fans → Maintenance utilities → final consolidation.
