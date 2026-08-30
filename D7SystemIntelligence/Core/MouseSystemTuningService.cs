using System.Runtime.InteropServices;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record WindowsMouseSettings(int PointerSpeed, int Threshold1, int Threshold2, int Acceleration, bool EnhancePointerPrecision);

public sealed class MouseSystemTuningService
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpiGetMouseSpeed = 0x0070;
    private const uint SpiSetMouseSpeed = 0x0071;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private readonly string _backupPath;

    public MouseSystemTuningService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "InputLab");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "windows-mouse-backup.json");
    }

    public WindowsMouseSettings Read()
    {
        var mouse = new int[3];
        if (!SystemParametersInfoArray(SpiGetMouse, 0, mouse, 0))
            throw new InvalidOperationException("تعذر قراءة إعدادات تسارع مؤشر Windows.");
        var speed = 10;
        if (!SystemParametersInfoInt(SpiGetMouseSpeed, 0, ref speed, 0))
            throw new InvalidOperationException("تعذر قراءة سرعة مؤشر Windows.");
        return new WindowsMouseSettings(speed, mouse[0], mouse[1], mouse[2], mouse[2] != 0);
    }

    public string ApplyCompetitiveBaseline()
    {
        var current = Read();
        File.WriteAllText(_backupPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));

        var noAcceleration = new[] { 0, 0, 0 };
        if (!SystemParametersInfoArray(SpiSetMouse, 0, noAcceleration, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException("Windows رفض تعطيل pointer acceleration.");

        var speed = 10;
        if (!SystemParametersInfoInt(SpiSetMouseSpeed, 0, ref speed, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException("Windows رفض ضبط سرعة المؤشر على baseline 10/20.");

        var after = Read();
        return $"تم تطبيق Competitive Windows baseline مع Backup: PointerSpeed {after.PointerSpeed}/20 • EPP {(after.EnhancePointerPrecision ? "ON" : "OFF")} • Acceleration {after.Acceleration}. ملاحظة: الألعاب التي تستخدم Raw Input قد تتجاوز هذه الإعدادات بالكامل.";
    }

    public string Restore()
    {
        if (!File.Exists(_backupPath)) return "لا توجد نسخة Windows mouse محفوظة من D7KT.";
        var backup = JsonSerializer.Deserialize<WindowsMouseSettings>(File.ReadAllText(_backupPath));
        if (backup == null) return "نسخة إعدادات الماوس غير قابلة للقراءة.";

        var mouse = new[] { backup.Threshold1, backup.Threshold2, backup.Acceleration };
        if (!SystemParametersInfoArray(SpiSetMouse, 0, mouse, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException("تعذر استعادة إعدادات acceleration.");
        var speed = backup.PointerSpeed;
        if (!SystemParametersInfoInt(SpiSetMouseSpeed, 0, ref speed, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException("تعذر استعادة سرعة المؤشر.");

        var after = Read();
        return $"تمت الاستعادة: PointerSpeed {after.PointerSpeed}/20 • EPP {(after.EnhancePointerPrecision ? "ON" : "OFF")} • Acceleration {after.Acceleration}.";
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoArray(uint uiAction, uint uiParam, [In, Out] int[] pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoInt(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);
}
