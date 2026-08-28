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
    private readonly DispatcherTimer _timer;
    private HardwareSnapshot? _last;

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_,_) => RefreshHardware();
        _timer.Start(); RefreshHardware();
        Loaded += async (_,_) => await ScanGames();
        Closed += (_,_) => _hardware.Dispose();
    }

    private void RefreshHardware()
    {
        try
        {
            _last = _hardware.Read();
            CpuText.Text = $"{_last.CpuLoad:0}%"; CpuSub.Text = $"{_last.CpuTemp:0} °C • {_last.CpuName}";
            GpuText.Text = $"{_last.GpuLoad:0}%"; GpuSub.Text = $"{_last.GpuTemp:0} °C • {_last.GpuName}";
            RamText.Text = $"{_last.RamLoad:0}%"; VramText.Text = _last.VramLoad.HasValue ? $"{_last.VramLoad:0}%" : "N/A";
            FansGrid.ItemsSource = _last.Fans;
            StatusText.Text = $"Live • {_last.Fans.Count(f=>f.Controllable)} controllable fan channel(s) • {DateTime.Now:T}";
        } catch (Exception ex) { StatusText.Text = "Telemetry: " + ex.Message; }
    }

    private void ShowOnly(UIElement page) { foreach (var p in new UIElement[]{DashboardPage,GamesPage,DiagnosticsPage,CodPage,FansPage,UpdatesPage}) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed; }
    private void ShowDashboard(object s, RoutedEventArgs e)=>ShowOnly(DashboardPage);
    private void ShowGames(object s,RoutedEventArgs e)=>ShowOnly(GamesPage);
    private void ShowDiagnostics(object s,RoutedEventArgs e)=>ShowOnly(DiagnosticsPage);
    private void ShowCod(object s,RoutedEventArgs e)=>ShowOnly(CodPage);
    private void ShowFans(object s,RoutedEventArgs e)=>ShowOnly(FansPage);
    private void ShowUpdates(object s,RoutedEventArgs e)=>ShowOnly(UpdatesPage);

    private async Task ScanGames() { StatusText.Text="Scanning launchers…"; GamesGrid.ItemsSource = await _launchers.ScanAsync(); StatusText.Text=$"Launcher scan complete • {GamesGrid.Items.Count} installations"; }
    private async void RescanGames(object s,RoutedEventArgs e)=>await ScanGames();
    private async void RunAllScan(object s,RoutedEventArgs e){ await ScanGames(); ShowOnly(DiagnosticsPage); await RunDiagnosticCore(); }
    private async void RunDiagnostic(object s,RoutedEventArgs e)=>await RunDiagnosticCore();
    private async Task RunDiagnosticCore(){ if(_last==null) RefreshHardware(); DiagnosticsList.Items.Clear(); foreach(var f in await _diagnostics.RunAsync(_last!)) DiagnosticsList.Items.Add($"[{f.Severity}] {f.Area} — {f.Title}\n{f.Detail}\n{f.Recommendation}"); }

    private void ScanCod(object s,RoutedEventArgs e){ var r=_cod.Locate(); CodInfo.Text=r.Message+Environment.NewLine+(r.Path??"")+Environment.NewLine+string.Join(Environment.NewLine,r.CurrentValues.Select(k=>$"{k.Key} = {k.Value}")); }
    private void ApplyCodBalanced(object s,RoutedEventArgs e)=>ApplyCod("Balanced");
    private void ApplyCodQuality(object s,RoutedEventArgs e)=>ApplyCod("Quality");
    private void ApplyCod(string mode){ var cores=Math.Max(1, Environment.ProcessorCount/2); var vram=8f; CodInfo.Text=_cod.Optimize(cores,vram,mode)+Environment.NewLine+_cod.Locate().Message; }

    private void SetAllFans(float pct){ if(_last==null)return; var msgs=new List<string>(); foreach(var f in _last.Fans.Where(x=>x.Controllable)) if(_hardware.SetFanControl(f.Id,pct,out var m)) msgs.Add(m); MessageBox.Show(msgs.Count>0?string.Join(Environment.NewLine,msgs):"No writable fan controls were exposed by this motherboard/GPU.","D7 Fan Control"); RefreshHardware(); }
    private void Fans45(object s,RoutedEventArgs e)=>SetAllFans(45);
    private void Fans70(object s,RoutedEventArgs e)=>SetAllFans(70);
    private void FansRestore(object s,RoutedEventArgs e){_hardware.RestoreFans();RefreshHardware();}

    private async void CheckUpdates(object s,RoutedEventArgs e){UpdatesOutput.Text="Scanning…";UpdatesOutput.Text=await SystemActions.RunWingetUpgradeScanAsync();}
    private async void UpdateApps(object s,RoutedEventArgs e){if(MessageBox.Show("Install all available Winget application updates now?","D7",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;UpdatesOutput.Text=await SystemActions.UpgradeAppsAsync();}
    private async void WindowsRepairScan(object s,RoutedEventArgs e){UpdatesOutput.Text="Running DISM ScanHealth and SFC verifyonly…";UpdatesOutput.Text=await SystemActions.RunWindowsRepairScanAsync();}
}
