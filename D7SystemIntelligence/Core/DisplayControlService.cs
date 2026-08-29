using System.Runtime.InteropServices;

namespace D7SystemIntelligence.Core;

public sealed record DisplayModeInfo(int Width, int Height, int RefreshRateHz, int BitsPerPixel)
{
    public override string ToString() => $"{Width}×{Height} @ {RefreshRateHz}Hz ({BitsPerPixel}-bit)";
}

public sealed record BrightnessInfo(bool Supported, uint Minimum, uint Current, uint Maximum, string Detail);

public sealed class DisplayControlService
{
    private DisplayModeInfo? _rollbackMode;

    public DisplayModeInfo? GetCurrentMode()
    {
        var mode = NewMode();
        return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode)
            ? new DisplayModeInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, mode.dmBitsPerPel)
            : null;
    }

    public IReadOnlyList<DisplayModeInfo> GetModesForCurrentResolution()
    {
        var current = GetCurrentMode();
        if (current == null) return [];

        var modes = new List<DisplayModeInfo>();
        for (var i = 0; ; i++)
        {
            var mode = NewMode();
            if (!EnumDisplaySettings(null, i, ref mode)) break;
            if (mode.dmPelsWidth != current.Width || mode.dmPelsHeight != current.Height) continue;
            if (mode.dmDisplayFrequency is < 30 or > 1000) continue;
            modes.Add(new DisplayModeInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, mode.dmBitsPerPel));
        }

        return modes
            .GroupBy(x => (x.Width, x.Height, x.RefreshRateHz, x.BitsPerPixel))
            .Select(g => g.First())
            .OrderByDescending(x => x.RefreshRateHz)
            .ThenByDescending(x => x.BitsPerPixel)
            .ToArray();
    }

    public string ApplyRefreshRate(int refreshRateHz)
    {
        var current = GetCurrentMode();
        if (current == null) return "تعذر قراءة وضع الشاشة الحالي.";
        if (refreshRateHz < 30 || refreshRateHz > 1000) return "قيمة Refresh Rate غير صالحة.";

        var supported = GetModesForCurrentResolution().Any(x => x.RefreshRateHz == refreshRateHz);
        if (!supported) return $"Windows لم يعرض {refreshRateHz}Hz كخيار مدعوم على الدقة الحالية {current.Width}×{current.Height}.";

        _rollbackMode ??= current;
        var mode = NewMode();
        mode.dmPelsWidth = current.Width;
        mode.dmPelsHeight = current.Height;
        mode.dmBitsPerPel = current.BitsPerPixel;
        mode.dmDisplayFrequency = refreshRateHz;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;

        var test = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL)
            return "اختبار Windows رفض الوضع قبل تطبيقه: " + Explain(test);

        var result = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        return result == DISP_CHANGE_SUCCESSFUL
            ? $"تم تطبيق {current.Width}×{current.Height} @ {refreshRateHz}Hz فعليًا. التغيير غير دائم على السجل ويمكن استعادته من D7 أو بإعادة التشغيل."
            : "فشل التطبيق: " + Explain(result);
    }

    public string ApplyMaximumRefresh()
    {
        var mode = GetModesForCurrentResolution().OrderByDescending(x => x.RefreshRateHz).FirstOrDefault();
        return mode == null ? "لا توجد أوضاع شاشة متاحة." : ApplyRefreshRate(mode.RefreshRateHz);
    }

    public string Restore()
    {
        if (_rollbackMode == null)
            return "لا توجد قيمة سابقة محفوظة في جلسة D7 الحالية.";

        var target = _rollbackMode;
        var mode = NewMode();
        mode.dmPelsWidth = target.Width;
        mode.dmPelsHeight = target.Height;
        mode.dmBitsPerPel = target.BitsPerPixel;
        mode.dmDisplayFrequency = target.RefreshRateHz;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
        var result = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        if (result == DISP_CHANGE_SUCCESSFUL)
        {
            _rollbackMode = null;
            return $"تمت استعادة {target.Width}×{target.Height} @ {target.RefreshRateHz}Hz.";
        }
        return "فشل الاسترجاع: " + Explain(result);
    }

    public BrightnessInfo ReadBrightness()
    {
        var handle = GetFirstPhysicalMonitor(out var detail);
        if (handle == IntPtr.Zero) return new BrightnessInfo(false, 0, 0, 0, detail);
        try
        {
            if (!GetMonitorBrightness(handle, out var min, out var current, out var max))
                return new BrightnessInfo(false, 0, 0, 0, "الشاشة لا تدعم DDC/CI Brightness أو أن DDC/CI معطل من قائمة الشاشة.");
            return new BrightnessInfo(true, min, current, max, $"DDC/CI متاح. النطاق {min}–{max}.");
        }
        finally { DestroyPhysicalMonitor(handle); }
    }

    public string SetBrightness(uint value)
    {
        var handle = GetFirstPhysicalMonitor(out var detail);
        if (handle == IntPtr.Zero) return detail;
        try
        {
            if (!GetMonitorBrightness(handle, out var min, out _, out var max))
                return "الشاشة لا توفر DDC/CI Brightness.";
            value = Math.Clamp(value, min, max);
            return SetMonitorBrightness(handle, value)
                ? $"تم ضبط سطوع الشاشة فعليًا إلى {value}."
                : "Windows/DXVA2 رفض تغيير السطوع. قد تحتاج تفعيل DDC/CI من قائمة الشاشة.";
        }
        finally { DestroyPhysicalMonitor(handle); }
    }

    private static IntPtr GetFirstPhysicalMonitor(out string detail)
    {
        IntPtr monitor = IntPtr.Zero;
        MonitorEnumProc callback = (h, _, _, _) => { monitor = h; return false; };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || monitor == IntPtr.Zero)
        {
            detail = "تعذر الوصول إلى HMONITOR للشاشة الرئيسية.";
            return IntPtr.Zero;
        }

        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
        {
            detail = "لم يتم العثور على Physical Monitor يدعم واجهة DXVA2.";
            return IntPtr.Zero;
        }

        var physical = new PHYSICAL_MONITOR[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
        {
            detail = "تعذر فتح Physical Monitor عبر DXVA2.";
            return IntPtr.Zero;
        }

        for (var i = 1; i < physical.Length; i++) DestroyPhysicalMonitor(physical[i].hPhysicalMonitor);
        detail = physical[0].szPhysicalMonitorDescription ?? "Physical Monitor";
        return physical[0].hPhysicalMonitor;
    }

    private static DEVMODE NewMode()
    {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return mode;
    }

    private static string Explain(int code) => code switch
    {
        DISP_CHANGE_BADMODE => "الوضع غير مدعوم.",
        DISP_CHANGE_FAILED => "تعذر تغيير وضع العرض.",
        DISP_CHANGE_RESTART => "Windows يطلب إعادة تشغيل لتطبيق الوضع.",
        DISP_CHANGE_BADFLAGS => "Flags غير صالحة.",
        DISP_CHANGE_BADPARAM => "معاملات غير صالحة.",
        _ => $"رمز Windows {code}."
    };

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_RESTART = 1;
    private const int DISP_CHANGE_FAILED = -1;
    private const int DISP_CHANGE_BADMODE = -2;
    private const int DISP_CHANGE_BADFLAGS = -4;
    private const int DISP_CHANGE_BADPARAM = -5;
    private const uint CDS_TEST = 0x00000002;
    private const int DM_BITSPERPEL = 0x00040000;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);
    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);
    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);
    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr handle, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr handle, uint value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
