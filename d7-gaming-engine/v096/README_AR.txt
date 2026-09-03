D7 Gaming Engine v0.9.6 — Stable Gaming Mode

هذه النسخة مخصصة لجهازك الحالي: Ryzen 5 3600 + RTX 2060 SUPER 8GB + RAM 16GB + Windows 10 19045 + شاشة 1080p/165Hz.

الهدف: عند تشغيل Call of Duty، يبدأ D7 جلسة لعب مؤقتة وقابلة للاسترجاع بدل تعديلات النظام الدائمة.

ما الذي يفعله تلقائيًا:
- يراقب cod26-cod.exe وعائلة عمليات Call of Duty.
- يفعّل Windows Game Mode أثناء الجلسة.
- يستخدم Windows High performance فقط إذا كانت الخطة الأصلية موجودة، ثم يعيد خطتك السابقة بعد إغلاق اللعبة.
- يرفع أولوية اللعبة إلى AboveNormal فقط لتجنب تجويع الصوت/الشبكة/Anti-Cheat.
- يخفض أولوية برامج التنزيل والتحديث في الخلفية.
- إذا أصبحت الذاكرة الحرة أقل من 4GB، يخفض أولوية Chrome بدل إغلاقه أو تنظيف RAM بالقوة.
- إذا نزلت الذاكرة الحرة تحت 2GB، يسجل تحذيرًا ولا يستخدم EmptyStandbyList أو memory purge لأن ذلك قد يزيد التقطيع.
- عند إغلاق اللعبة، يعيد خطة الطاقة وأولويات العمليات وإعداد Game Mode كما كانت.
- يعمل مع Windows مباشرة من EXE بدون Scheduled PowerShell tasks أو نوافذ PowerShell.

ما لا يفعله:
- لا CPU Sets أو Affinity تلقائي.
- لا EcoQoS للعبة.
- لا Timer Resolution 1ms.
- لا BCD/HPET tweaks.
- لا custom D7 power plan.
- لا تعطيل Anti-Cheat ولا تعديل ذاكرة اللعبة.
- لا حذف برامجك أو إغلاق Discord.

يوجد داخل التطبيق زر لإظهار إعدادات COD المقترحة لجهازك بدقة 1080p/165Hz.