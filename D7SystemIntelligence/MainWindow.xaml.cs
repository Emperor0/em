using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public partial class MainWindow : Window
{
    private readonly HardwareEngine _hardware = new();
    private readonly LauncherScanner _launchers = new();
    private readonly DiagnosticsEngine _diagnostics = new();
    private readonly CodAdapter _cod = new();
    private readonly D7Orchestrator _orchestrator = new();
    private readonly NetworkIntelligence _network = new();
    private readonly PeripheralIntelligence _peripherals = new();
    private readonly DriverIntelligence _drivers = new();
    private readonly DispatcherTimer _timer;
    private HardwareSnapshot? _last;
    private bool _observing;

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_,_) => RefreshHardware();
        _timer.Start();
        RefreshHardware();
        Loaded += async (_,_) => await ScanGames();
        Closed += (_,_) => _hardware.Dispose();
    }

    private void RefreshHardware()
    {
        try
        {
            _last = _hardware.Read();
            CpuText.Text = $"{_last.CpuLoad:0}%";
            CpuSub.Text = $"{_last.CpuTemp:0} °C • {_last.CpuName}";
            GpuText.Text = $"{_last.GpuLoad:0}%";
            GpuSub.Text = $"{_last.GpuTemp:0} °C • {_last.GpuName}";
            RamText.Text = $"{_last.RamLoad:0}%";
            VramText.Text = _last.VramLoad.HasValue ? $"{_last.VramLoad:0}%" : "غير متاح";
            FansGrid.ItemsSource = _last.Fans;

            if (!_observing) _ = RefreshOrchestratorAsync(_last);
        }
        catch (Exception ex)
        {
            StatusText.Text = "خطأ في قراءة الجهاز: " + ex.Message;
        }
    }

    private async Task RefreshOrchestratorAsync(HardwareSnapshot snapshot)
    {
        _observing = true;
        try
        {
            _orchestrator.Profile = AutopilotBox.SelectedIndex switch
            {
                0 => D7Profile.Safe,
                2 => D7Profile.MaxPerformance,
                _ => D7Profile.Balanced
            };

            var status = await _orchestrator.ObserveAsync(snapshot);
            ModeText.Text = D7Orchestrator.ModeArabic(status.Context.Mode);
            MissionText.Text = status.Context.Reason;
            PolicyText.Text = status.Summary;
            StatusText.Text = $"مباشر • {D7Orchestrator.ModeArabic(status.Context.Mode)} • {snapshot.Fans.Count(f=>f.Controllable)} قناة مراوح قابلة للتحكم • {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "محرك D7: " + ex.Message;
        }
        finally
        {
            _observing = false;
        }
    }

    private void ShowOnly(UIElement page)
    {
        foreach (var p in new UIElement[]
        {
            DashboardPage, GamesPage, DiagnosticsPage, NetworkPage, PeripheralsPage,
            DriversPage, CodPage, FansPage, UpdatesPage
        })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowDashboard(object s, RoutedEventArgs e)=>ShowOnly(DashboardPage);
    private void ShowGames(object s,RoutedEventArgs e)=>ShowOnly(GamesPage);
    private void ShowDiagnostics(object s,RoutedEventArgs e)=>ShowOnly(DiagnosticsPage);
    private void ShowNetwork(object s,RoutedEventArgs e)=>ShowOnly(NetworkPage);
    private void ShowPeripherals(object s,RoutedEventArgs e)=>ShowOnly(PeripheralsPage);
    private void ShowDrivers(object s,RoutedEventArgs e)=>ShowOnly(DriversPage);
    private void ShowCod(object s,RoutedEventArgs e)=>ShowOnly(CodPage);
    private void ShowFans(object s,RoutedEventArgs e)=>ShowOnly(FansPage);
    private void ShowUpdates(object s,RoutedEventArgs e)=>ShowOnly(UpdatesPage);

    private async Task ScanGames()
    {
        StatusText.Text="جاري فحص الألعاب والمنصات…";
        var games = await _launchers.ScanAsync();
        GamesGrid.ItemsSource = games;
        _orchestrator.SetKnownGames(games);
        StatusText.Text=$"اكتمل فحص المنصات • تم اكتشاف {games.Count} تثبيت";
    }

    private async void RescanGames(object s,RoutedEventArgs e)=>await ScanGames();

    private async void RunAllScan(object s,RoutedEventArgs e)
    {
        await ScanGames();
        await ScanNetworkCore();
        ShowOnly(DiagnosticsPage);
        await RunDiagnosticCore();
    }

    private async void RunDiagnostic(object s,RoutedEventArgs e)=>await RunDiagnosticCore();

    private async Task RunDiagnosticCore()
    {
        if(_last==null) RefreshHardware();
        DiagnosticsList.Items.Clear();

        foreach(var f in await _diagnostics.RunAsync(_last!))
            DiagnosticsList.Items.Add($"[{f.Severity}] {f.Area} — {f.Title}\n{f.Detail}\n{f.Recommendation}");

        if (_orchestrator.LastStatus is { } live)
        {
            foreach (var d in live.Decisions)
                DiagnosticsList.Items.Add($"[{SeverityArabic(d.Severity)}] {AreaArabic(d.Area)} — {d.Title}\n{d.Detail}");
        }
    }

    private async void ScanNetwork(object s, RoutedEventArgs e)=>await ScanNetworkCore();

    private async Task ScanNetworkCore()
    {
        NetworkNotesText.Text = "جاري قياس الشبكة واللاتنسي…";
        try
        {
            var r = await _network.ScanAsync();
            NetAdapterText.Text = $"{r.AdapterName}\n{r.IPv4}";
            NetSpeedText.Text = r.LinkSpeedBps > 0 ? $"{r.LinkSpeedBps / 1_000_000.0:0} Mbps" : "غير متاح";
            NetLatencyText.Text = $"{FormatMs(r.InternetLatencyMs)} / {FormatMs(r.JitterMs)}";
            NetLossText.Text = $"{r.PacketLossPercent:0.#}%";
            NetworkNotesText.Text = $"زمن الوصول للبوابة: {FormatMs(r.GatewayLatencyMs)}\n{r.Notes}";
        }
        catch (Exception ex)
        {
            NetworkNotesText.Text = "تعذر إكمال فحص الشبكة: " + ex.Message;
        }
    }

    private async void ScanPeripherals(object s, RoutedEventArgs e)
    {
        StatusText.Text = "جاري فحص الماوس والكيبورد واليد والشاشة والصوت…";
        var list = await _peripherals.ScanAsync();
        PeripheralsGrid.ItemsSource = list;
        StatusText.Text = $"تم اكتشاف {list.Count} جهاز طرفي / HID حاضر";
    }

    private async void ScanDrivers(object s, RoutedEventArgs e)
    {
        DriverSummaryText.Text = "جاري قراءة التعريفات الفعلية…";
        var list = await _drivers.ScanAsync();
        DriversGrid.ItemsSource = list;
        DriverSummaryText.Text = list.Count == 0 ? "تعذر قراءة قائمة التعريفات." : DriverIntelligence.BuildSummary(list);
    }

    private void ScanCod(object s,RoutedEventArgs e)
    {
        var r=_cod.Locate();
        CodInfo.Text=r.Message+Environment.NewLine+(r.Path??"")+Environment.NewLine+string.Join(Environment.NewLine,r.CurrentValues.Select(k=>$"{k.Key} = {k.Value}"));
    }

    private void ApplyCodBalanced(object s,RoutedEventArgs e)=>ApplyCod("Balanced");
    private void ApplyCodQuality(object s,RoutedEventArgs e)=>ApplyCod("Quality");

    private void ApplyCod(string mode)
    {
        var cores=Math.Max(1, Environment.ProcessorCount/2);
        var vram=8f;
        CodInfo.Text=_cod.Optimize(cores,vram,mode)+Environment.NewLine+_cod.Locate().Message;
    }

    private void SetAllFans(float pct)
    {
        if(_last==null)return;
        var msgs=new List<string>();
        foreach(var f in _last.Fans.Where(x=>x.Controllable))
            if(_hardware.SetFanControl(f.Id,pct,out var m)) msgs.Add(m);
        MessageBox.Show(msgs.Count>0?string.Join(Environment.NewLine,msgs):"اللوحة أو كرت الشاشة لم يوفرا قناة مراوح قابلة للكتابة. D7 لن يفرض تحكمًا غير مدعوم.","D7 — التحكم بالمراوح");
        RefreshHardware();
    }

    private void Fans45(object s,RoutedEventArgs e)=>SetAllFans(45);
    private void Fans70(object s,RoutedEventArgs e)=>SetAllFans(70);
    private void FansRestore(object s,RoutedEventArgs e){_hardware.RestoreFans();RefreshHardware();}

    private async void CheckUpdates(object s,RoutedEventArgs e)
    {
        UpdatesOutput.Text="جاري فحص تحديثات التطبيقات…";
        UpdatesOutput.Text=await SystemActions.RunWingetUpgradeScanAsync();
    }

    private async void UpdateApps(object s,RoutedEventArgs e)
    {
        if(MessageBox.Show("هل تريد تثبيت تحديثات التطبيقات المتاحة عبر Winget الآن؟","D7",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        UpdatesOutput.Text=await SystemActions.UpgradeAppsAsync();
    }

    private async void WindowsRepairScan(object s,RoutedEventArgs e)
    {
        UpdatesOutput.Text="جاري تشغيل DISM ScanHealth و SFC VerifyOnly…";
        UpdatesOutput.Text=await SystemActions.RunWindowsRepairScanAsync();
    }

    private static string FormatMs(double? value)=>value.HasValue ? $"{value.Value:0.0} ms" : "غير متاح";

    private static string SeverityArabic(string value)=>value.ToLowerInvariant() switch
    {
        "critical" => "حرج",
        "warning" => "تحذير",
        "info" => "معلومة",
        "ok" => "سليم",
        _ => value
    };

    private static string AreaArabic(string value)=>value.ToLowerInvariant() switch
    {
        "thermal" => "الحرارة",
        "memory" => "الذاكرة",
        "performance" => "الأداء",
        "streaming" => "البث",
        "fans" => "المراوح",
        "profile" => "الوضع",
        "stability" => "الاستقرار",
        _ => value
    };
}
