# D7KT Feature Value Audit — 2026

هذا الملف Gate إلزامي للنسخة الكبيرة. وجود زر/صفحة لا يعني أن الميزة ناجحة.

## Automated release gate — PASS
- Pre-release gate **#203** completed successfully on Windows after excluding the retired `MainWindow*` shell from the product build.
- `dotnet publish` self-contained single EXE: **PASS**.
- Production D7KT shell construction + exact **6-center navigation contract**: **PASS**.
- Retired pre-D7KT `MainWindow.xaml` / `MainWindow*.cs`: kept only in source history and **excluded from compiled/package output**.
- Inno Setup compile: **PASS**.
- Clean silent install into a real Windows path: **PASS**.
- Health check from the installed EXE: **PASS**.
- Self-update overwrite path: previous EXE backup + post-update health check + recovery log verification: **PASS**.
- Release workflow reruns the same gates before publishing; a failed gate blocks the public release.
- This automated gate does **not** pretend to validate vendor/hardware-dependent runtime paths such as OpenRGB devices, monitor DDC/CI, OBS configuration, writable fan controllers, real game frametimes, or driver changes on the user's machine.

## حالات القرار
- **KEEP**: لها قيمة واضحة وتنفيذ حقيقي.
- **MERGE**: القيمة موجودة لكن لا تستحق صفحة مستقلة.
- **CONDITIONAL**: تبقى فقط عند توفر دعم الهاردوير/الـAPI الحقيقي.
- **REMOVE**: لا قيمة عملية كافية أو مجرد UI/Vanity.
- **RUNTIME PENDING**: الكود/CI لا يكفيان؛ تحتاج اختبار الجهاز المستهدف قبل Production-ready.

## Definition of Done
1. Use case واضح.
2. تنفيذ فعلي وليس Text/Button فقط.
3. Supported / Read-only / Unavailable صريحة.
4. Before/After إذا كانت الميزة تدعي تحسين الأداء.
5. Backup/Restore لأي تعديل مستمر أو حساس.
6. Verify/read-back بعد الكتابة متى كان ذلك ممكنًا.
7. لا أرقام أو Claims غير قابلة للقياس.
8. مقارنة بمنافس مباشر وسبب واضح لوجود D7KT.
9. Build + Installer + Runtime validation قبل Production-ready.

## Current audit — big release branch

| Feature | Decision | Current value / quality gate | Runtime |
|---|---|---|---|
| Self Update | KEEP / HARDENED | Download progress + SHA-256 + visible installer + previous EXE backup + post-update shell/core healthcheck + automatic executable rollback on failed healthcheck | automated success path PASS; device update still to validate |
| RGB Studio | KEEP | Per-device color/mode/brightness, scenes, OpenRGB backend, runtime/game/mission intelligence; no fake unsupported zones | PENDING device matrix test |
| Input Lab | KEEP | Raw Input polling distribution/jitter/stalls, NKRO, controller drift/range, Windows pointer baseline + restore; no fake generic DPI writes | PENDING device test |
| Shadow Capture | KEEP | OBS Replay ownership, no duplicate recorder, duration/folder/hotkey, metadata, safe cleanup, performance/OBS impact test, rollback of D7-owned OBS changes | PENDING OBS/game test |
| Mission Engine | KEEP | Applied/Verified/AlreadyOptimal/Unsupported/Failed states; changes owned by D7 only; restore only what was actually changed | PENDING game mission test |
| Diagnostics + Action Center | KEEP | Stable codes/evidence, real safe repair routes, no fake WHEA/GPU auto-fix | PENDING repair test |
| Stutter Black Box | KEEP | Shared PresentMon session, no duplicate monitor, raw frametime evidence + stutter correlation | PENDING game test |
| Benchmark Lab | KEEP | Raw frametimes; FPS/1%/0.1%/P95/P99/P99.9; confidence + KEEP/REJECT/NO PROOF workflow | PENDING repeatability test |
| Stream Director | KEEP | OBS stats + render/encoding/network diagnosis correlated with game CPU/GPU/P99 and OBS→VirtualCam→TikTok chain | PENDING stream test |
| HUD | KEEP | Adaptive click-through HUD using shared RuntimeBus; removed second PresentMon/network scanner | PENDING game test |
| App Intelligence | KEEP | Discord/Steam/NVIDIA App/OBS/TikTok/Chrome/Edge profiles, verified priority, safe startup/cache, mission integration, restore; protected NVIDIA/voice paths | PENDING installed-app test |
| Network Lab | KEEP | PC/NIC→Router→ISP→DNS→Remote route diagnosis; optional endpoint; manual bufferbloat; verified NIC writes; Before/After + automatic rollback on clear regression | PENDING network test |
| Display | KEEP | Hz mode validation, CDS_TEST, read-back verify, auto rollback, persistent Restore Vault; DDC/CI brightness with verify | PENDING monitor test |
| Audio | KEEP | Endpoint inventory, volume/mute/default-role writes with read-back verification and persistent default-role restore | PENDING audio-device test |
| Driver Safety | KEEP | Driver Store backup, Restore Point attempt, Windows Update driver path, before/after inventory verification, no blind newest-driver claim | PENDING driver test |
| Storage Center | KEEP | Windows Storage Reliability, temp/health/free/errors, persistent reliability deltas, Analyze/ReTrim without fake performance claim | PENDING drive test |
| Crash Investigator | KEEP | WHEA/GPU/Storage/App/Kernel-Power filtering + temporal evidence chains; correlation explicitly not causation | PENDING event-log test |
| Smart Fans | CONDITIONAL KEEP | Writable-channel gate, hysteresis, emergency 100%, read-back software-control verification, default restore on failure/exit | likely Read-only on current motherboard until proven otherwise |
| Restore Vault | KEEP | Persistent recovery data used by display/audio/network/drivers/startup and other D7-owned changes | PENDING cross-feature restore test |
| Startup Manager | MERGE INTO MAINTENANCE | Real Run/StartupApproved/folder management + Restore Vault. Useful, but not worthy of top-level page | PENDING runtime |
| Background Apps | MERGE INTO MAINTENANCE / MISSIONS | Protected/Keep/Review/SafeToClose classification, user policy, no generic blind process kill | PENDING runtime/benchmark |
| Smart Removal | MERGE INTO MAINTENANCE | Preview-first removal/leftovers engine remains specialized maintenance action, not product pillar | PENDING destructive-flow sandbox test |
| Safe Maintenance | KEEP AS CENTER | Scan→Plan→Apply; refuses heavy app/driver updates while Gaming/Streaming; winget + verified driver path; no BIOS/Firmware | PENDING Windows test |
| Clip Library | MERGE INTO CAPTURE | Metadata/library/rename/move/delete/trim/storage policy belongs under Shadow Capture | PENDING clip test |
| Auto Scene | MERGE INTO MISSIONS | Policy layer for automatic mission switching; not a standalone product | PENDING game transition test |
| Performance Contract | MERGE INTO MISSIONS/BENCHMARK | Measurable runtime target/guard, not separate navigation pillar | PENDING stress test |
| COD Adapter | CONDITIONAL KEEP | Only known schema keys, backup, game-closed guard; no arbitrary config edits | PENDING current COD schema test |
| Launcher Scanner / Game Identity | KEEP AS INFRA | Steam/Epic exact manifests where available, Xbox/Ubisoft/fallback, persistent user-confirmed executable identity; avoids treating heuristic EXE as truth | PENDING installed-library scan |
| Raw peripheral inventory | MERGE | Infrastructure feeding Input/Audio/Display/RGB; no raw-list product page needed | N/A |
| Raw driver inventory | MERGE | Infrastructure feeding Driver Safety/Diagnostics; no duplicate Device Manager page | N/A |

## Explicit removals / non-features
- Generic “FPS tweak” buttons with no measurement: **REMOVE**.
- Generic registry/network tweak packs: **REMOVE**.
- Fake generic DPI/polling firmware writes: **REMOVE** unless a real vendor adapter exists.
- Fake HDR/VRR/G-SYNC controls without a verified read/write path: **REMOVE from controls**; read-only evidence is acceptable.
- Unsafe EC/Super-I/O/PWM fan control: **REMOVE / prohibited**.
- “Newest driver = best driver” logic: **REMOVE**.
- Generic process killer that touches unknown/system/anti-cheat/driver/audio processes: **REMOVE**.
- Duplicate PresentMon/encoder/network monitors when shared telemetry already exists: **REMOVE**.
- Separate navigation pages for infrastructure-only features: **MERGE**.
- Retired pre-D7KT shell in compiled product: **REMOVE** (source history retained only).

## Final consolidation
واجهة المستخدم النهائية قليلة وواضحة. التخصصات تفتح كـTools/Dialogs داخل المراكز، لا تتحول كل ميزة إلى صفحة Sidebar.

المراكز النهائية:
1. Dashboard
2. Health + Repair
3. Gaming + Performance
4. Devices + Apps
5. Capture + Stream
6. Updates + Maintenance

داخلها تندمج Auto Scene / Performance Contract / Clip Library / Startup / Background / Smart Removal / raw inventories بدل تضخيم التنقل.

## Release policy
- كل العمل يبقى على `d7-system-intelligence-build`; لا merge إلى `main` ضمن هذا الإصدار.
- Public release لا ينشر إلا إذا Publish + Shell Health + 6-center contract + Installer + clean-install smoke + self-update smoke كلها PASS.
- CI يثبت البناء والتثبيت ومسار التحديث؛ لا يثبت أن RGB/DDC/OBS/Audio/Fans/Drivers تعمل على كل جهاز.
- بعد وصول الإصدار إلى الجهاز المستهدف يبدأ hardware/runtime validation feature-by-feature؛ أي مسار يفشل يُصلح بدل وصفه بأنه جاهز.

## Device validation queue after delivery
1. Launch/version/UI contract.
2. Shadow Capture: OBS WebSocket → Replay Buffer → F8 → actual clip/folder/duration.
3. Input Lab polling/controller tests.
4. Game detection + PresentMon session + Benchmark/HUD.
5. Mission/Auto Scene + restore ownership.
6. Network Before/After and bufferbloat manual test.
7. Display/Audio verified writes and restore.
8. RGB only on detected/supported OpenRGB devices.
9. Fans remain Read-only unless a verified writable channel exists.
10. Driver/maintenance flows only outside active gaming/streaming sessions.
