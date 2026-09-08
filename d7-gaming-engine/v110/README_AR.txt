D7 Gaming Engine v1.1.0 — Gaming + Streaming Edition

نسخة مخصصة لجهاز ألعاب وبث، مع هدفين: تقليل حمل D7 نفسه، وتقليل حمل Windows والخدمات الخلفية بدون تويكات قديمة أو غير قابلة للاسترجاع.

أهم التغييرات:
- Live loop خفيف جدًا: CPU/RAM من Win32 مباشرة بدون WMI/CIM مستمر.
- GPU polling كل 5 ثوانٍ أثناء اللعب فقط.
- وضع ألعاب + بث بنقرة واحدة مع Backup/Restore.
- Game Mode ON، Background Capture OFF، Visual Effects أقل، Background Store Apps أقل، Edge background/startup boost OFF.
- High Performance power plan عند تفعيل الوضع.
- اللعبة وOBS إلى AboveNormal أثناء الجلسة، والمحدثات المعروفة إلى BelowNormal فقط.
- D7 نفسه ينزل إلى BelowNormal أثناء اللعب حتى لا ينافس اللعبة.
- Deep Scan فقط هو الذي يستخدم WMI/CIM.
- تحديث ONLINE ONLY مع SHA-256.

متعمد عدم لمس: Defender، Pagefile، SysMain، HPET/BCD/Timer، RAM purge، RealTime priority، VBS/HVCI، أو registry network hacks.
