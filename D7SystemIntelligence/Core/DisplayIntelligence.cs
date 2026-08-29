using System.Runtime.InteropServices;

namespace D7SystemIntelligence.Core;

public sealed record DisplaySnapshot(
    int Width,
    int Height,
    int RefreshRateHz,
    int BitsPerPixel,
    bool RefreshGuardOk,
    int TargetRefreshRateHz,
    string Summary);

public sealed class DisplayIntelligence
{
    public DisplaySnapshot Read(int targetRefreshRateHz = 165)
    {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf<DEVMODE>();

        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode))
        {
            return new DisplaySnapshot(0, 0, 0, 0, false, targetRefreshRateHz,
                "تعذر قراءة وضع الشاشة الحالي من Windows.");
        }

        var refresh = Math.Max(0, mode.dmDisplayFrequency);
        var ok = refresh >= Math.Max(60, targetRefreshRateHz - 2);
        var summary = ok
            ? $"الشاشة تعمل على {mode.dmPelsWidth}×{mode.dmPelsHeight} @ {refresh}Hz. Refresh Guard سليم."
            : $"تنبيه: الشاشة تعمل على {refresh}Hz بينما الهدف {targetRefreshRateHz}Hz.";

        return new DisplaySnapshot(
            mode.dmPelsWidth,
            mode.dmPelsHeight,
            refresh,
            mode.dmBitsPerPel,
            ok,
            targetRefreshRateHz,
            summary);
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string? lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
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
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
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
