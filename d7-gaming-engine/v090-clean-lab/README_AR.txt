D7 Gaming Engine v0.9.0 — Clean Lab

هذه نسخة اختبار معزولة لقلب v0.9.0 الأصلي.

الهدف:
اختبار هل v0.9.0 نفسه يسبب الثقل/التقطيع بعد إزالة تأثير أي نسخ D7 قديمة كانت تعمل من Task Scheduler أو ProgramData.

ما تمت إضافته حول v0.9.0:
- Preflight يعطل فقط مهام D7 القديمة المؤكدة من التقرير قبل تشغيل الاختبار.
- يبدأ الاختبار من Windows Balanced.
- لا ينشئ Clean Lab أي Startup أو Scheduled Task أو Service.
- بعد إغلاق v0.9.0 ينفذ Rollback تلقائياً ويرجع Windows Balanced ويحذف D7 GAME PERFORMANCE إن بقيت.
- Watchdog يوقف الجلسة إذا سجل explorer.exe أعطالاً متكررة أثناء الاختبار.
- D7_Emergency_Stop.exe لإيقاف v0.9.0 فوراً واسترجاع الإعدادات الأساسية.
- قناة تحديث Lab مستقلة؛ لا تغير قناة Stable الحالية.

مهم:
قلب الأداء نفسه ما زال v0.9.0 Adaptive Core الأصلي: HighQoS/Above Normal للعبة، EcoQoS وعزل الخلفية، CPU Sets، Timer Resolution 1ms، وخطة D7 GAME PERFORMANCE عند تحقق شروطه. الهدف هنا أن نختبر هذه الأشياء وحدها بدون تداخل الأنظمة القديمة.

طريقة الاختبار:
1) أعد تشغيل Windows بعد تنظيف مهام D7 القديمة.
2) شغّل D7_Gaming_Engine_v0.9.0_Clean_Lab.exe كمسؤول.
3) افتح COD والعب مباراة فعلية 10-15 دقيقة.
4) إذا ظهر تقطيع/ثقل قوي شغّل D7_Emergency_Stop.exe فوراً.
5) إذا كان كل شيء طبيعي، أغلق D7 بشكل عادي وسيتم Rollback تلقائياً.

لا تشغل D7_Gaming_Engine_Core_v0.9.0.exe مباشرة؛ شغّل Clean Lab Launcher فقط حتى يكون الاختبار معزولاً وقابلاً للاسترجاع.
