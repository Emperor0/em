using D7SystemIntelligence.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class GameOverlayWindow : Window, IAsyncDisposable
{
    private readonly HardwareEngine _hardware;
    private readonly NetworkIntelligence _network=new();
    private readonly ManagedPresentMonService _presentMon=new();
    private readonly FrameMetricsMonitor _frames;
    private readonly string _processName;
    private readonly System.Windows.Controls.TextBlock _text=new();
    private readonly DispatcherTimer _timer;
    private double? _ping;
    private double? _jitter;
    private DateTime _lastNetwork=DateTime.MinValue;
    private bool _networkBusy;
    private int _pid;

    public GameOverlayWindow(HardwareEngine hardware,string processName)
    {
        _hardware=hardware;_processName=processName;_frames=_presentMon.CreateMonitor();
        Title="D7 HUD";Width=390;Height=88;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;Focusable=false;Left=12;Top=12;
        var border=new System.Windows.Controls.Border{Background=new SolidColorBrush(Color.FromArgb(178,8,10,14)),CornerRadius=new CornerRadius(8),Padding=new Thickness(10),Child=_text};
        _text.Foreground=Brushes.White;_text.FontFamily=new FontFamily("Consolas");_text.FontSize=14;_text.Text="D7 HUD • تجهيز PresentMon…";
        Content=border;
        _timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};_timer.Tick+=(_,_)=>Tick();
        SourceInitialized+=(_,_)=>MakeClickThrough();
        Loaded+=async(_,_)=>await StartAsync();
        Closed+=async(_,_)=>await DisposeAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var process=Process.GetProcessesByName(_processName).OrderByDescending(x=>SafeWorkingSet(x)).FirstOrDefault();
            if(process==null){_text.Text=$"D7 HUD • {_processName} غير شغالة";return;}
            _pid=process.Id;process.Dispose();
            var progress=new Progress<double>(p=>_text.Text=$"D7 HUD • تجهيز PresentMon {p:0}%");
            await _presentMon.EnsureAsync(progress);
            await _frames.StartAsync(_pid);
            _timer.Start();
        }
        catch(Exception ex){_text.Text="D7 HUD: "+ex.Message;}
    }

    private void Tick()
    {
        try
        {
            if(!IsProcessAlive(_pid)){_text.Text=$"D7 HUD • {_processName} أغلقت";_timer.Stop();return;}
            var h=_hardware.Read();var f=_frames.Read();
            var fps=f?.Fps;var low=f?.OnePercentLow;var p99=f?.P99FrameMs;
            var ping=_ping.HasValue?$"{_ping:0}ms":"—";
            var first=fps.HasValue?$"{fps:0} FPS | 1% {low:0} | P99 {p99:0.0}ms | Ping {ping}":$"FPS … | Ping {ping}";
            var pressure=h.CpuLoad>=90||h.GpuLoad>=97||h.CpuTemp>=82||h.GpuTemp>=82||h.RamLoad>=88||(p99.HasValue&&p99.Value>=25);
            _text.Text=pressure
                ? first+$"\nCPU {h.CpuLoad:0}% {h.CpuTemp:0}° | GPU {h.GpuLoad:0}% {h.GpuTemp:0}° | RAM {h.RamLoad:0}% | Jitter {(_jitter.HasValue?$"{_jitter:0.0}ms":"—")}" 
                : first;
            Height=pressure?88:52;
            if(!_networkBusy&&(DateTime.UtcNow-_lastNetwork).TotalSeconds>=5)_=RefreshNetworkAsync();
        }
        catch(Exception ex){_text.Text="D7 HUD: "+ex.Message;}
    }

    private async Task RefreshNetworkAsync()
    {
        _networkBusy=true;
        try{var n=await _network.ScanAsync();_ping=n.InternetLatencyMs;_jitter=n.JitterMs;_lastNetwork=DateTime.UtcNow;}catch{}finally{_networkBusy=false;}
    }

    private void MakeClickThrough()
    {
        var hwnd=new WindowInteropHelper(this).Handle;
        var ex=GetWindowLongPtr(hwnd,GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd,GWL_EXSTYLE,new IntPtr(ex|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE));
    }

    private static long SafeWorkingSet(Process p){try{return p.WorkingSet64;}catch{return 0;}}
    private static bool IsProcessAlive(int pid){try{using var p=Process.GetProcessById(pid);return !p.HasExited;}catch{return false;}}
    public async ValueTask DisposeAsync(){_timer.Stop();await _frames.DisposeAsync();}

    private const int GWL_EXSTYLE=-20;private const long WS_EX_TRANSPARENT=0x20;private const long WS_EX_TOOLWINDOW=0x80;private const long WS_EX_NOACTIVATE=0x08000000;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd,int nIndex);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern IntPtr GetWindowLong32(IntPtr hWnd,int nIndex);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd,int nIndex,IntPtr dwNewLong);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")]private static extern IntPtr SetWindowLong32(IntPtr hWnd,int nIndex,IntPtr dwNewLong);
    private static IntPtr GetWindowLongPtr(IntPtr hWnd,int nIndex)=>IntPtr.Size==8?GetWindowLongPtr64(hWnd,nIndex):GetWindowLong32(hWnd,nIndex);
    private static IntPtr SetWindowLongPtr(IntPtr hWnd,int nIndex,IntPtr value)=>IntPtr.Size==8?SetWindowLongPtr64(hWnd,nIndex,value):SetWindowLong32(hWnd,nIndex,value);
}
