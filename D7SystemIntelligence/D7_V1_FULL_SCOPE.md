# D7 System Intelligence — V1 Comprehensive Scope

هذا الملف يثبت نطاق التحديث الشامل القادم. لا يعتبر أي بند مكتملًا لمجرد وجود زر أو صفحة؛ المطلوب تنفيذ فعلي + تحقق + قياس + رجوع آمن حيث ينطبق.

## التحديث الذاتي لـ D7 — ميزة إلزامية
- زر واضح داخل التطبيق: «التحقق من تحديث D7» ثم «تحديث D7 الآن».
- عند الضغط: قراءة أحدث Release -> مقارنة الإصدار -> عرض الملاحظات -> تنزيل المثبت -> التحقق من SHA-256 -> تشغيل المثبت بصمت -> إغلاق D7 -> التثبيت فوق النسخة الحالية -> إعادة تشغيل D7 تلقائيًا.
- لا يحتاج المستخدم الرجوع للمحادثة أو تنزيل ملف يدوي بعد تثبيت النسخة الداعمة.
- فحص اختياري عند بدء التشغيل مع إشعار فقط؛ لا تثبيت حساس بدون موافقة المستخدم.
- Stable/Beta channels لاحقًا، مع سجل إصدار وRollback عندما يصبح آمنًا ومدعومًا.
- Pipeline النشر ينشئ GitHub Release تلقائيًا مع ملف EXE وSHA-256 لكل إصدار D7 رسمي.

## Core / Autopilot
- Orchestrator/Background core لحالة الجهاز.
- أوضاع: خمول، عادي، لعب، Ranked، بث، لعب+بث، تسجيل، Creator.
- Missions: PRO RANKED / STREAM+RANKED / MAX FPS / STORY / RECORD / SILENT / FULL HEALTH / UPDATE EVERYTHING SAFE.
- Policy Engine + Action/Recovery Engine + Restore Vault.
- Telemetry محلي + Session history + Timeline.
- Performance Contract: هدف FPS/Latency/Recording budget/Quality.

## Gaming / Performance
- FPS / 1% Low / frametime / P95/P99 حيث يتوفر القياس.
- CPU/GPU/RAM/VRAM/Clock/Temperature/Power/Encoder telemetry.
- Bottleneck detection مع Confidence.
- Stutter Black Box قبل/بعد التقطيع.
- Regression detection بين الجلسات.
- Per-game profiles وTarget FPS engine.
- Competitive/Balanced/Cinematic policies.
- Launcher adapters: Steam, Battle.net, Xbox, Epic, EA, Ubisoft, Rockstar, GOG, Amazon وغيرها عبر مصادر موثقة.
- Config adapters مع backup/verify/replace فقط للمفاتيح المعروفة.
- Anti-cheat Safe Mode.

## D7 Shadow Capture / Replay — أولوية قصوى
- Replay Buffer بمدة يحددها المستخدم: 15/30/45/60/120/300 ثانية ومخصص.
- اختيار مجلد الحفظ العام أو لكل لعبة.
- Hotkeys متعددة لكل مدة.
- استخدام Hardware Encoder الأنسب (NVENC/AMF/QSV) مع حماية موارد اللعبة.
- Resource Budget: حد لاستهلاك التسجيل؛ يخفض preset/bitrate تلقائيًا إذا أثر على اللعب.
- عدم الكتابة المستمرة للقرص قدر الإمكان؛ الحفظ عند طلب المقطع.
- منع ازدواج التسجيل إذا OBS/TikTok/Recorder آخر يعمل.
- Audio tracks اختيارية: Game / Mic / Discord حيث تسمح البنية.
- Clip Library حسب اللعبة والتاريخ مع rename/move/delete/trim بسيط.
- Auto naming وAuto cleanup حسب مساحة يحددها المستخدم.
- Replay indicator صغير جدًا وقابل للإخفاء.
- Moment Marker وحفظ مقطع حول العلامة لاحقًا.
- قياس تأثير التسجيل على frametime و1% low قبل اعتماد إعداداته.

## Streaming Director
- اكتشاف OBS/TikTok LIVE Studio وحالة البث.
- مراقبة encoder load/render lag/dropped frames/network pressure.
- Profiles: 1080p50/60, Competitive Stream, Quality Stream.
- Dual-goal optimizer: FPS target + stream stability.
- Background Governor وتعارض الـrecorders/overlays.
- تقرير بعد البث.

## Overlay / Competitive HUD
- OSD صغير جدًا وقابل للتخصيص.
- Minimal: FPS + Ping.
- Full: FPS, 1% low, frametime, CPU/GPU, temps, RAM/VRAM, ping/jitter/loss.
- Adaptive visibility وتحذير مؤقت فقط عند المشكلة.
- Frametime mini graph وmarkers.

## Peripheral / Pro Input Lab
- تجميع HID المتكرر إلى أجهزة حقيقية مفهومة.
- Mouse: polling measurement/stability/jitter/USB path/power saving/raw-input checks.
- Controller: stick drift/deadzone/trigger range/circularity/polling/wired-vs-Bluetooth.
- Keyboard: polling/USB/NKRO diagnostics؛ Rapid Trigger فقط عبر دعم رسمي.
- USB topology map.
- Safe Peripheral Tuning + rollback عند أي override مدعوم.
- Per-game peripheral profiles.

## Display Intelligence
- Refresh-rate guard مثل 165Hz.
- VRR/G-SYNC/HDR/bit depth/RGB range/scaling detection.
- DDC/CI controls عندما تكون الشاشة تدعمها.
- Profiles: Competitive/Desktop/Cinema/Creator.
- ICC awareness؛ لا ادعاء calibration احترافي بدون colorimeter.

## RGB Studio
- تكامل OpenRGB/واجهات رسمية للأجهزة المدعومة.
- مزامنة case/board/RAM/GPU/fans/keyboard/mouse.
- Profiles حسب الوضع/اللعبة.
- Temperature-reactive RGB وAmbient mode اختياري.

## Audio Intelligence
- كشف الأجهزة الفعلية والافتراضية مثل Astro/Sonar.
- sample rate/routing/communications/spatial-audio checks.
- DPC/ISR/audio latency diagnostics.
- Competitive Audio وStreaming Audio profiles.

## Driver Intelligence
- جرد مهم فقط: GPU/Chipset/LAN/Audio/USB/Bluetooth/Monitor/Peripheral.
- مقارنة الإصدار الحالي مع المصدر الرسمي.
- Driver history + benchmark baseline + stability observation.
- GPU driver A/B testing حيث يكون قابلًا للتنفيذ بأمان.
- Rollback واضح.
- Clean/minimal NVIDIA install مع الحفاظ على NVENC والمكونات المطلوبة.
- BIOS/Firmware updates تبقى حساسة وتتطلب موافقة واضحة.

## Network Intelligence
- ping/jitter/loss/gateway/DNS/bufferbloat/NIC diagnostics.
- Network process view لمعرفة من يستهلك الاتصال.
- Gaming Network Governor يؤجل downloads/cloud sync/update jobs أثناء اللعب.
- قبل/بعد لأي tuning؛ التعديل الذي لا يحسن القياس يرجع.

## Thermal / Fans
- قراءة RPM/temps.
- AUTO curves ديناميكية فقط للقنوات التي يثبت أنها قابلة للكتابة.
- Hysteresis + predictive thermal response + emergency cooling.
- Restore BIOS/AUTO عند الخروج أو crash.
- لا كتابة EC/Super-I/O عمياء للأجهزة غير المدعومة.

## Windows / Apps / Maintenance
- Windows Update awareness + منع reboot/تحميل ثقيل أثناء اللعب.
- Winget app updates.
- Startup intelligence.
- Background scheduler.
- DISM/SFC/health checks.
- Event Viewer/Reliability/Crash investigation.
- Storage SMART/health/temperature/free-space/I/O.
- Memory pressure/pagefile/leak diagnostics.
- App policies لـ Discord/Chrome/Steam/OBS/TikTok وغيرها.
- زر «تحديث كل شيء الآمن» مع استثناء التحديثات الحساسة التي تحتاج موافقة.

## UX / Product Quality
- عربي RTL كامل + English.
- واجهة Mission Control لا قوائم معلومات خام.
- صفحات جهازية فعلية لكل Mouse/Controller/Headset/Display.
- Notification Center غير مزعج.
- Tray mode + startup option.
- Logs + diagnostic package.
- Security hardening + signed update path + checksums.
- لا placebo tweaks: كل تعديل يجب أن يملك سببًا وقياسًا وRollback حيث ينطبق.

## Definition of Done
الإصدار الشامل لا يسمى مكتملًا إلا بعد نجاح build، installer، self-update path، وتشغيل الوظائف الأساسية بدون أخطاء تجميع؛ أما الوظائف المعتمدة على هاردوير محدد فيتم إظهار حالة Supported/Read-only/Unavailable بوضوح بدل ادعاء دعم غير موجود.