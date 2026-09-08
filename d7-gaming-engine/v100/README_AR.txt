D7 Gaming Engine v1.0.0
Adaptive Performance Engine

هذا الإصدار يحول D7 من أداة إعدادات ثابتة إلى محرك أداء تكيفي يراقب الجهاز أثناء اللعب ويصنف سبب الضغط ثم يتدخل فقط عند الحاجة.

المراقبة الحالية:
- CPU الكلي وCPU الخاص باللعبة.
- NVIDIA GPU utilization / temperature / VRAM / clock / power عند توفر nvidia-smi.
- RAM الكلية والمتاحة واستهلاك اللعبة.
- اكتشاف الألعاب تلقائيًا + تعلم EXE للعبة يدويًا عند الحاجة.

الحالات الذكية:
GPU_BOUND / CPU_BOUND / THERMAL_LIMIT / MEMORY_PRESSURE / BACKGROUND_INTERFERENCE / BALANCED.

الأمان:
كل تغييرات الجلسة قابلة للاسترجاع. لا يتم استخدام Real-Time Priority أو HPET/BCD/Timer tweaks أو RAM purge أو تعديل ذاكرة اللعبة أو Anti-Cheat.

التحديث:
التحديث ONLINE ONLY من قناة stable في GitHub. D7 يتحقق من latest.json، ينزل ZIP، يتحقق من SHA-256 ثم يستبدل EXE ويعيد تشغيله. لا يوجد مسار تحديث يدوي من ملف محلي.

السجل وملفات التعلم:
%ProgramData%\D7 Gaming Engine
