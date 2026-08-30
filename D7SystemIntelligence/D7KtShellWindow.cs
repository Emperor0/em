using D7SystemIntelligence.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class D7KtShellWindow : Window
{
    private const int HotkeyId = 0xD7A1;
    private const int WmHotkey = 0x0312;

    private readonly HardwareEngine _hardware = new();
    private readonly D7Orchestrator _orchestrator = new();
    private readonly LauncherScanner _launchers = new();
    private readonly DiagnosticsEngine _diagnostics = new();
    private readonly D7ActionCenterService _actions = new();
    private readonly D7UpdateService _updates = new();
    private readonly NetworkIntelligence _network = new();
    private readonly NetworkGamingProfileService _networkProfile = new();
    private readonly ShadowCaptureService _shadow = new();
    private readonly FullHealthCheckService _health;
    private readonly D7MissionEngine _missions;
    private readonly GameSessionService _sessions;
    private readonly BenchmarkLabService _benchmark;
    private readonly PerformanceContractSettingsStore _contractStore = new();
    private readonly PerformanceContractService _contract;
    private readonly AutoSceneSettingsStore _sceneStore = new();
    private readonly AutoSceneDirector _scene;
    private readonly SmartFanController _fans;
    private readonly CodAdapter _cod = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ContentControl _host = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _nav = new(StringComparer.OrdinalIgnoreCase);

    private readonly TextBlock _status = new();
    private readonly TextBlock _mode = new();
    private readonly TextBlock _cpu = new();
    private readonly TextBlock _gpu = new();
    private readonly TextBlock _ram = new();
    private readonly TextBlock _vram = new();
    private readonly TextBlock _cpuSub = new();
    private readonly TextBlock _gpuSub = new();
    private readonly TextBlock _healthScore = new();
    private readonly TextBlock _healthSummary = new();
    private readonly StackPanel _findings = new();
    private readonly TextBlock _missionStatus = new();
    private readonly TextBlock _sceneStatus = new();
    private readonly TextBlock _fanStatus = new();
    private readonly TextBlock _networkStatus = new();
    private readonly TextBlock _captureStatus = new();
    private readonly TextBlock _updateStatus = new();
    private readonly ProgressBar _updateProgress = new() { Minimum = 0, Maximum = 100, Height = 7 };
    private readonly TextBlock _footerStats = new();

    private HardwareSnapshot? _lastHardware;
    private GameOverlayWindow? _hud;
    private HwndSource? _hotkeySource;
    private IntPtr _hwnd;
    private bool _tickBusy;
    private bool _sessionBusy;
    private bool _sceneBusy;
    private bool _updateBusy;
    private DateTimeOffset _lastNetwork = DateTimeOffset.MinValue;
    private string _sceneMessage = "Auto Scene جاهز.";

    public D7KtShellWindow()
    {
        _health = new FullHealthCheckService(_hardware);
        _missions = new D7MissionEngine(_hardware);
        _sessions = new GameSessionService(_hardware);
        _benchmark = new BenchmarkLabService(_sessions);
        _contract = new PerformanceContractService(_sessions, _hardware, _shadow);
        _scene = new AutoSceneDirector(_sceneStore);
        _fans = new SmartFanController(_hardware);

        Title = "D7KT • System Intelligence";
        Width = 1500;
        Height = 880;
        MinWidth = 1180;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = B("Bg");
        Foreground = B("Text");
        Icon = D7KtBrand.CreateIcon();
        FlowDirection = FlowDirection.RightToLeft;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(7),
            CornerRadius = new CornerRadius(14),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        Content = BuildShell();
        BuildPages();
        Go("dashboard");

        _missions.StatusChanged += text => Dispatcher.Invoke(() =>
        {
            _missionStatus.Text = text;
            SetStatus(text);
        });
        _sessions.StatusChanged += text => Dispatcher.Invoke(() => SetStatus(text));
        _fans.StatusChanged += text => Dispatcher.Invoke(() =>
        {
            _fanStatus.Text = text;
            SetStatus(text);
        });
        _contract.StatusChanged += text => Dispatcher.Invoke(() =>
        {
            if (text.StartsWith("CONTRACT", StringComparison.OrdinalIgnoreCase)) SetStatus(text);
        });

        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => await StartAsync();
        SourceInitialized += (_, _) => RegisterHotkey();
        Closed += async (_, _) => await ShutdownAsync();
    }

    private UIElement BuildShell()
    {
        var root = new Grid { Background = B("Bg") };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        var title = TitleBar();
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var body = new Grid { Margin = new Thickness(12, 0, 12, 8) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(216) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = Navigation();
        Grid.SetColumn(nav, 0);
        body.Children.Add(nav);
        _host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _host.VerticalContentAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(_host, 2);
        body.Children.Add(_host);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Border
        {
            Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 4, 16, 4)
        };
        var fg = new Grid();
        fg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Text = "D7KT Engine يبدأ…";
        _status.Foreground = B("Muted");
        _status.FontSize = 11.5;
        _footerStats.Foreground = B("Muted");
        _footerStats.FontSize = 11.5;
        _footerStats.FlowDirection = FlowDirection.LeftToRight;
        Grid.SetColumn(_status, 0);
        Grid.SetColumn(_footerStats, 1);
        fg.Children.Add(_status);
        fg.Children.Add(_footerStats);
        footer.Child = fg;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private UIElement TitleBar()
    {
        var bar = new Border
        {
            Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 0, 14, 0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(BrandPart("D", B("Text"), 22));
        brand.Children.Add(BrandPart("7", B("Accent"), 22));
        brand.Children.Add(BrandPart("KT", B("Text"), 22));
        brand.Children.Add(new TextBlock { Text = "  SYSTEM INTELLIGENCE", FontSize = 10, Foreground = B("Muted"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(brand, 0);
        grid.Children.Add(brand);

        _mode.Text = "الوضع الطبيعي";
        _mode.Foreground = B("Muted");
        _mode.FontWeight = FontWeights.SemiBold;
        _mode.HorizontalAlignment = HorizontalAlignment.Center;
        _mode.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_mode, 1);
        grid.Children.Add(_mode);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        controls.Children.Add(TitleButton("—", () => WindowState = WindowState.Minimized));
        controls.Children.Add(TitleButton("□", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        controls.Children.Add(TitleButton("×", Close, true));
        Grid.SetColumn(controls, 2);
        grid.Children.Add(controls);
        bar.Child = grid;
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else DragMove();
        };
        return bar;
    }

    private UIElement Navigation()
    {
        var box = new Border
        {
            Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(10)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(35, 5, 8), Color.FromRgb(12, 12, 14), 90),
            BorderBrush = B("AccentSoft"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15),
            Padding = new Thickness(13), Margin = new Thickness(0, 0, 0, 10)
        };
        var logoStack = new StackPanel { FlowDirection = FlowDirection.LeftToRight };
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        line.Children.Add(BrandPart("D", B("Text"), 27));
        line.Children.Add(BrandPart("7", B("Accent"), 27));
        line.Children.Add(BrandPart("KT", B("Text"), 27));
        logoStack.Children.Add(line);
        logoStack.Children.Add(new TextBlock { Text = "SYSTEM INTELLIGENCE • 2026", FontSize = 9, Foreground = B("Muted"), Margin = new Thickness(2, 3, 0, 0) });
        logo.Child = logoStack;
        Grid.SetRow(logo, 0);
        grid.Children.Add(logo);

        var nav = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        Nav(nav, "dashboard", "⌂", "لوحة التحكم", "نظرة واحدة لكل شيء");
        Nav(nav, "health", "✚", "التشخيص والإصلاح", "اكتشاف + إجراء فعلي");
        Nav(nav, "gaming", "◈", "الألعاب والأداء", "Missions وAuto Scene");
        Nav(nav, "devices", "⌁", "الأجهزة والنظام", "Display • RGB • Audio");
        Nav(nav, "capture", "◉", "التقاط وبث", "Replay • HUD • Stream");
        Nav(nav, "updates", "↻", "التحديثات والإعدادات", "D7 + Windows + Apps");
        Grid.SetRow(nav, 1);
        grid.Children.Add(nav);

        var version = new Border { Background = B("Panel2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(11), Margin = new Thickness(0, 8, 0, 0) };
        version.Child = new TextBlock { Text = $"D7KT {_updates.CurrentVersionText}\n● Local Engine", FontSize = 10.5, Foreground = B("Muted"), FlowDirection = FlowDirection.LeftToRight };
        Grid.SetRow(version, 2);
        grid.Children.Add(version);
        box.Child = grid;
        return box;
    }

    private void BuildPages()
    {
        _pages["dashboard"] = Dashboard();
        _pages["health"] = Health();
        _pages["gaming"] = Gaming();
        _pages["devices"] = Devices();
        _pages["capture"] = Capture();
        _pages["updates"] = Updates();
    }

    private UIElement Dashboard()
    {
        var root = PageStack();
        var hero = new Border
        {
            Height = 172, Background = D7KtBrand.HeroBrush(), BorderBrush = B("AccentSoft"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 12)
        };
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        var hs = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        brand.Children.Add(BrandPart("D", B("Text"), 46)); brand.Children.Add(BrandPart("7", B("Accent"), 46)); brand.Children.Add(BrandPart("KT", B("Text"), 46));
        hs.Children.Add(brand);
        hs.Children.Add(new TextBlock { Text = "SYSTEM INTELLIGENCE", FontSize = 11, Foreground = B("Muted"), FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(4, -4, 0, 7) });
        hs.Children.Add(new TextBlock { Text = "مركز واحد للأداء، التشخيص، الإصلاح، الألعاب، البث، الأجهزة والتحديثات — بدون Tweaks وهمية.", FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 700 });
        Grid.SetColumn(hs, 0); hg.Children.Add(hs);
        var ha = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        ha.Children.Add(Action("فحص شامل الآن", async () => await DiagnoseAsync(true), true));
        ha.Children.Add(Action("PRO RANKED", async () => await MissionAsync(D7Mission.ProRanked)));
        Grid.SetColumn(ha, 1); hg.Children.Add(ha);
        hero.Child = hg;
        root.Children.Add(hero);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 0, -4, 10) };
        metrics.Children.Add(Metric("CPU", _cpu, _cpuSub));
        metrics.Children.Add(Metric("GPU", _gpu, _gpuSub));
        metrics.Children.Add(Metric("RAM", _ram, Small("ضغط الذاكرة المباشر")));
        metrics.Children.Add(Metric("VRAM", _vram, Small("ذاكرة كرت الشاشة")));
        root.Children.Add(metrics);

        var middle = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 0, -4, 10) };
        var hcard = Card();
        var hs2 = new StackPanel(); hs2.Children.Add(Section("التشخيص الذكي", "المشكلة + هل يمكن إصلاحها + الإجراء الصحيح."));
        _healthScore.Text = "—"; _healthScore.FontSize = 42; _healthScore.FontWeight = FontWeights.Black; _healthScore.Foreground = B("Accent");
        _healthSummary.Text = "لم يتم الفحص بعد"; _healthSummary.Foreground = B("Muted"); _healthSummary.TextWrapping = TextWrapping.Wrap;
        hs2.Children.Add(_healthScore); hs2.Children.Add(_healthSummary); hs2.Children.Add(Action("فتح مركز الإصلاح", () => { Go("health"); return Task.CompletedTask; }, true)); hcard.Child = hs2;
        middle.Children.Add(hcard);
        var mcard = Card(); var ms = new StackPanel(); ms.Children.Add(Section("الوضع الذكي", "Mission Engine ينسق Power + Display + Network + Fans + Replay مع Restore."));
        _missionStatus.Text = "لا توجد Mission نشطة."; _missionStatus.Foreground = B("Muted"); _missionStatus.TextWrapping = TextWrapping.Wrap; _missionStatus.Margin = new Thickness(0, 12, 0, 8);
        ms.Children.Add(_missionStatus); ms.Children.Add(Action("فتح الألعاب والأداء", () => { Go("gaming"); return Task.CompletedTask; })); mcard.Child = ms; middle.Children.Add(mcard);
        root.Children.Add(middle);

        var quick = new UniformGrid { Columns = 4, Margin = new Thickness(-4) };
        quick.Children.Add(Tool("التقاط المقاطع", "Replay خفيف • Hotkey • مجلد تختاره", () => Go("capture")));
        quick.Children.Add(Tool("الأجهزة", "Display • RGB • Audio • Input", () => Go("devices")));
        quick.Children.Add(Tool("التحديثات", "زر واحد لتحديث D7KT", () => Go("updates")));
        quick.Children.Add(Tool("Restore Vault", "رجوع آمن للتغييرات", () => Open(new RestoreVaultWindow())));
        root.Children.Add(quick);
        return Scroll(root);
    }

    private UIElement Health()
    {
        var root = PageStack(); root.Children.Add(Header("التشخيص والإصلاح", "كل Finding يوضح إذا كان إصلاحه آمنًا، يحتاج مركزًا متخصصًا، أو قراءة فقط."));
        var top = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 12, -4, 10) };
        top.Children.Add(Hero("فحص ذكي", "Hardware + Event Logs + RAM/VRAM", "فحص", async () => await DiagnoseAsync(false)));
        top.Children.Add(Hero("Windows Repair", "DISM RestoreHealth + SFC /scannow", "إصلاح", async () => await AutoRepairAsync(D7RepairRoute.WindowsRepair)));
        top.Children.Add(Hero("تنظيف آمن", "Temp أقدم من 24 ساعة فقط", "تنظيف", async () => await AutoRepairAsync(D7RepairRoute.TempCleanup)));
        top.Children.Add(Hero("Full Health", "Storage + Stability + Integrity", "فتح", () => { Open(new FullHealthWindow(_health)); return Task.CompletedTask; }));
        root.Children.Add(top);
        _findings.Children.Clear(); _findings.Children.Add(Empty("اضغط فحص. النتائج ستظهر هنا مع زر إجراء مناسب.")); root.Children.Add(_findings);
        return Scroll(root);
    }

    private UIElement Gaming()
    {
        var root = PageStack(); root.Children.Add(Header("الألعاب والأداء", "اختيار هدف واحد بدل عشرات Tweaks. كل Mission لها Apply + Restore."));
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 12, -4, 10) };
        grid.Children.Add(MissionCard("PRO RANKED", "Power + أعلى Hz + Gaming Network + تنظيف خلفية آمن.", D7Mission.ProRanked));
        grid.Children.Add(MissionCard("STREAM + RANKED", "Governor للبث + Network + Replay الموجود فقط.", D7Mission.StreamRanked));
        grid.Children.Add(MissionCard("RECORDING", "Shadow Capture + Power + Fans حسب الدعم.", D7Mission.Recording));
        grid.Children.Add(MissionCard("STORY / ULTRA", "أعلى Refresh وتبريد ذكي للألعاب القصصية.", D7Mission.Story));
        grid.Children.Add(MissionCard("SILENT", "Balanced + استعادة Fan overrides.", D7Mission.Silent));
        grid.Children.Add(Hero("RESTORE", "إيقاف Mission وإرجاع الإعدادات المحفوظة.", "استعادة", async () => { var r = await _missions.RestoreAsync(); SetStatus(r.Summary); }));
        root.Children.Add(grid);

        var scene = Card(); var ss = new StackPanel(); ss.Children.Add(Section("Auto Scene", "يحدد رانك/قصة/بث تلقائيًا بعد Stability Delay."));
        _sceneStatus.Text = SceneText(); _sceneStatus.Foreground = B("Muted"); _sceneStatus.TextWrapping = TextWrapping.Wrap; _sceneStatus.Margin = new Thickness(0, 10, 0, 8); ss.Children.Add(_sceneStatus);
        var sr = new WrapPanel(); sr.Children.Add(Action("تشغيل / إيقاف", ToggleScene, true)); sr.Children.Add(Action("إعدادات", () => { Open(new AutoSceneWindow(_scene, SceneText)); return Task.CompletedTask; })); ss.Children.Add(sr); scene.Child = ss; root.Children.Add(scene);

        var labs = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 10, -4, 10) };
        labs.Children.Add(Tool("Mission Control", "تفاصيل كل خطوة وRestore", () => Open(new MissionControlWindow(_missions, Game))));
        labs.Children.Add(Tool("Performance Contract", "FPS/حرارة/ضغط مع Guard", () => Open(new PerformanceContractWindow(_contract, _contractStore))));
        labs.Children.Add(Tool("Benchmark Lab", "Baseline → Change → Compare", () => Open(new BenchmarkLabWindow(_benchmark))));
        labs.Children.Add(Tool("Stutter Black Box", "FPS • 1% • P99 • Stutters", () => Open(new SessionHistoryWindow(_sessions))));
        root.Children.Add(labs);

        var cod = Card(); var cs = new StackPanel(); cs.Children.Add(Section("Call of Duty Adapter", "يعدل فقط مفاتيح Schema المعروفة ويأخذ Backup."));
        var cr = new WrapPanel();
        cr.Children.Add(Action("فحص COD", () => { var r = _cod.Locate(); MessageBox.Show(r.Message + "\n" + (r.Path ?? ""), "D7KT • COD"); return Task.CompletedTask; }));
        cr.Children.Add(Action("Competitive", () => { MessageBox.Show(_cod.Optimize(Math.Max(1, Environment.ProcessorCount / 2), 8f, "Balanced"), "D7KT • COD"); return Task.CompletedTask; }, true));
        cr.Children.Add(Action("Quality / 165", () => { MessageBox.Show(_cod.Optimize(Math.Max(1, Environment.ProcessorCount / 2), 8f, "Quality"), "D7KT • COD"); return Task.CompletedTask; }));
        cs.Children.Add(cr); cod.Child = cs; root.Children.Add(cod);
        return Scroll(root);
    }

    private UIElement Devices()
    {
        var root = PageStack(); root.Children.Add(Header("الأجهزة والنظام", "الأدوات القوية مجمعة حسب المهمة بدل Sidebar مليان صفحات."));
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 12, -4, 10) };
        grid.Children.Add(Tool("الشاشة", "Refresh / Display Apply & Restore", () => Open(new DisplayControlWindow())));
        grid.Children.Add(Tool("RGB Studio", "OpenRGB للأجهزة المدعومة", () => Open(new RgbStudioWindow(_hardware))));
        grid.Children.Add(Tool("Audio Studio", "الأجهزة الفعلية + Sonar", () => Open(new AudioStudioWindow())));
        grid.Children.Add(Tool("Input Lab", "Polling • Drift • Controller/HID", () => Open(new InputLabWindow())));
        grid.Children.Add(Tool("Driver Safety", "Backup • Scan • Rollback path", () => Open(new DriverSafetyWindow())));
        grid.Children.Add(Tool("Storage Center", "Health • Reliability • ReTrim", () => Open(new StorageCenterWindow())));
        grid.Children.Add(Tool("Startup Manager", "إدارة بدء التشغيل", () => Open(new StartupManagerWindow())));
        grid.Children.Add(Tool("Background Apps", "تصنيف + تنظيف ذكي", () => Open(new BackgroundAppsWindow())));
        grid.Children.Add(Tool("Smart Removal", "فحص قبل حذف الجذور", () => Open(new SmartRemovalWindow())));
        grid.Children.Add(Tool("Crash Investigator", "WHEA • GPU • Disk • Shutdown", () => Open(new CrashInvestigatorWindow())));
        grid.Children.Add(Tool("Restore Vault", "إدارة النسخ والرجوع", () => Open(new RestoreVaultWindow())));
        grid.Children.Add(Tool("Full Health", "صحة الجهاز في تقرير واحد", () => Open(new FullHealthWindow(_health))));
        root.Children.Add(grid);

        var fan = Card(); var fs = new StackPanel(); fs.Children.Add(Section("Smart Fans", "لن يكتب D7KT PWM إلا إذا ظهرت قناة writable حقيقية.")); _fanStatus.Text = "جاري قراءة القنوات…"; _fanStatus.Foreground = B("Muted"); _fanStatus.Margin = new Thickness(0, 9, 0, 7); fs.Children.Add(_fanStatus);
        var fr = new WrapPanel(); fr.Children.Add(Action("تشغيل AUTO", () => { var ok = _fans.Start(); _fanStatus.Text = ok ? "AUTO Fan يعمل." : "لا توجد قناة writable آمنة."; return Task.CompletedTask; }, true)); fr.Children.Add(Action("إيقاف + استعادة", () => { _fans.Stop(true); _fanStatus.Text = "تمت الاستعادة إلى BIOS/AUTO."; return Task.CompletedTask; })); fs.Children.Add(fr); fan.Child = fs; root.Children.Add(fan);

        var net = Card(); var ns = new StackPanel(); ns.Children.Add(Section("Network Intelligence", "قياس أولًا ثم Gaming Network مع Backup/Restore.")); _networkStatus.Text = "لم يتم الفحص بعد."; _networkStatus.Foreground = B("Muted"); _networkStatus.TextWrapping = TextWrapping.Wrap; _networkStatus.Margin = new Thickness(0, 9, 0, 7); ns.Children.Add(_networkStatus);
        var nr = new WrapPanel(); nr.Children.Add(Action("قياس الآن", async () => await NetworkAsync(true))); nr.Children.Add(Action("Gaming Network", ApplyNetworkAsync, true)); nr.Children.Add(Action("استعادة", RestoreNetworkAsync)); ns.Children.Add(nr); net.Child = ns; root.Children.Add(net);
        return Scroll(root);
    }

    private UIElement Capture()
    {
        var root = PageStack(); root.Children.Add(Header("التقاط وبث", "Replay واحد عبر OBS بدل Recorder ثاني يضغط GPU/CPU."));
        var card = Card(); var cs = new StackPanel(); cs.Children.Add(Section("D7 Shadow Capture", "مدة + مجلد + Hotkey. تحفظ آخر المدة فقط.")); _captureStatus.Text = "جاري قراءة Replay…"; _captureStatus.Foreground = B("Muted"); _captureStatus.TextWrapping = TextWrapping.Wrap; _captureStatus.Margin = new Thickness(0, 9, 0, 8); cs.Children.Add(_captureStatus);
        var row = new WrapPanel(); row.Children.Add(Action("تشغيل Replay", StartReplay, true)); row.Children.Add(Action("حفظ آخر مقطع", SaveReplay, true)); row.Children.Add(Action("إيقاف", StopReplay)); row.Children.Add(Action("الإعدادات", () => { Open(new ShadowCaptureWindow(_shadow)); RegisterHotkey(); return Task.CompletedTask; })); row.Children.Add(Action("مكتبة المقاطع", () => { Open(new ClipLibraryWindow(() => _shadow.LoadSettings().SaveFolder)); return Task.CompletedTask; })); cs.Children.Add(row); card.Child = cs; root.Children.Add(card);
        var tools = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 10, -4, 0) };
        tools.Children.Add(Tool("D7 HUD", "FPS • 1% • Frametime • Temps", ToggleHud));
        tools.Children.Add(Tool("Stream Director", "OBS/TikTok + Process Governor", () => Open(new StreamDirectorWindow(Game()))));
        tools.Children.Add(Tool("Session History", "تقارير اللعب والتقطيع", () => Open(new SessionHistoryWindow(_sessions))));
        root.Children.Add(tools); return Scroll(root);
    }

    private UIElement Updates()
    {
        var root = PageStack(); root.Children.Add(Header("التحديثات والإعدادات", "D7KT يتحدث من داخل نفسه: تنزيل → SHA-256 → Installer مرئي → إعادة فتح."));
        var up = Card(); var us = new StackPanel(); us.Children.Add(Section("D7KT Self Update", "إذا فشل أي جزء يظهر السبب بدل الصمت.")); _updateStatus.Text = $"الإصدار الحالي: {_updates.CurrentVersionText}"; _updateStatus.Foreground = B("Muted"); _updateStatus.Margin = new Thickness(0, 9, 0, 6); _updateStatus.TextWrapping = TextWrapping.Wrap; us.Children.Add(_updateStatus); _updateProgress.Margin = new Thickness(0, 0, 0, 8); us.Children.Add(_updateProgress);
        var ur = new WrapPanel(); ur.Children.Add(Action("فحص وتحديث الآن", () => UpdateAsync(true, false), true)); ur.Children.Add(Action("فحص فقط", () => UpdateAsync(false, false))); us.Children.Add(ur); up.Child = us; root.Children.Add(up);
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 10, -4, 0) };
        grid.Children.Add(Hero("تحديث التطبيقات", "Winget upgrade --all", "تحديث", async () => Result("Apps Update", await SystemActions.UpgradeAppsAsync())));
        grid.Children.Add(Hero("Windows Integrity", "DISM ScanHealth + SFC VerifyOnly", "فحص", async () => Result("Windows Scan", await SystemActions.RunWindowsRepairScanAsync())));
        grid.Children.Add(Hero("Windows Repair", "RestoreHealth + SFC /scannow", "إصلاح", async () => await AutoRepairAsync(D7RepairRoute.WindowsRepair)));
        grid.Children.Add(Hero("فحص القرص", "CHKDSK /scan", "فحص", async () => Result("Disk Check", await SystemActions.CheckSystemDriveAsync())));
        grid.Children.Add(Hero("Flush DNS", "تنظيف DNS cache فقط", "تنفيذ", async () => Result("DNS", await SystemActions.FlushDnsAsync())));
        grid.Children.Add(Hero("Restore Vault", "راجع النسخ والاستعادة", "فتح", () => { Open(new RestoreVaultWindow()); return Task.CompletedTask; }));
        root.Children.Add(grid); return Scroll(root);
    }

    private async Task StartAsync()
    {
        SetStatus("تشغيل D7KT Engine…");
        try { var games = await _launchers.ScanAsync(); _orchestrator.SetKnownGames(games); SetStatus($"D7KT جاهز • {games.Count} تثبيت لعبة/منصة"); }
        catch (Exception ex) { SetStatus("Launcher scan: " + ex.Message); }
        var contract = _contractStore.Load(); if (contract.Enabled) _contract.Start(contract);
        _timer.Start();
        await TickAsync();
        _ = UpdateAsync(false, true);
        _ = RefreshCapture();
    }

    private async Task TickAsync()
    {
        if (_tickBusy) return; _tickBusy = true;
        try
        {
            var h = _hardware.Read(); _lastHardware = h;
            _cpu.Text = $"{h.CpuLoad:0}%"; _gpu.Text = $"{h.GpuLoad:0}%"; _ram.Text = $"{h.RamLoad:0}%"; _vram.Text = h.VramLoad.HasValue ? $"{h.VramLoad:0}%" : "—";
            _cpuSub.Text = $"{h.CpuTemp:0}°C • {h.CpuName}"; _gpuSub.Text = $"{h.GpuTemp:0}°C • {h.GpuName}";
            _fanStatus.Text = h.Fans.Count == 0 ? "لا توجد قنوات مرصودة." : $"قنوات {h.Fans.Count} • writable {h.Fans.Count(x => x.Controllable)}";
            var live = await _orchestrator.ObserveAsync(h); _mode.Text = $"{D7Orchestrator.ModeArabic(live.Context.Mode)} • {D7MissionEngine.MissionArabic(_missions.ActiveMission)}"; _missionStatus.Text = live.Summary;
            _footerStats.Text = $"CPU {h.CpuLoad:0}%  •  GPU {h.GpuLoad:0}%  •  RAM {h.RamLoad:0}%  •  {DateTime.Now:HH:mm:ss}";
            await SyncSession(live.Context.PrimaryGame); await SceneAsync(live.Context);
            if ((DateTimeOffset.Now - _lastNetwork).TotalSeconds >= 20) { _lastNetwork = DateTimeOffset.Now; _ = NetworkAsync(false); }
        }
        catch (Exception ex) { SetStatus("Live Engine: " + ex.Message); }
        finally { _tickBusy = false; }
    }

    private async Task SyncSession(string? game)
    {
        if (_sessionBusy) return; _sessionBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(game)) { if (_sessions.IsRunning) await _sessions.StopAsync(); return; }
            if (_sessions.IsRunning && string.Equals(_sessions.ActiveGame, game, StringComparison.OrdinalIgnoreCase)) return;
            if (_sessions.IsRunning) await _sessions.StopAsync();
            await _sessions.StartAsync(game);
        }
        catch (Exception ex) { SetStatus("Stutter Black Box: " + ex.Message); }
        finally { _sessionBusy = false; }
    }

    private async Task SceneAsync(RuntimeContext context)
    {
        if (_sceneBusy) return; _sceneBusy = true;
        try
        {
            var e = _scene.Evaluate(context, _missions.ActiveMission); _sceneMessage = e.Reason; _sceneStatus.Text = SceneText();
            if (!e.Ready) return;
            if (e.Target == D7Mission.None) await _missions.RestoreAsync(); else await _missions.ApplyAsync(e.Target, context.PrimaryGame);
        }
        catch (Exception ex) { _sceneMessage = "Auto Scene: " + ex.Message; }
        finally { _sceneBusy = false; }
    }

    private async Task DiagnoseAsync(bool navigate)
    {
        if (navigate) Go("health");
        _findings.Children.Clear(); _findings.Children.Add(Empty("جاري الفحص…"));
        try
        {
            var h = _lastHardware ?? _hardware.Read(); var raw = await _diagnostics.RunAsync(h); var list = _actions.Classify(raw).ToList(); list.Insert(0, D7ActionCenterService.WindowsRepairCard()); list.Insert(1, D7ActionCenterService.TempCleanupCard());
            _findings.Children.Clear(); foreach (var f in list) _findings.Children.Add(Finding(f));
            var critical = raw.Count(x => x.Severity.Contains("حرج", StringComparison.OrdinalIgnoreCase) || x.Severity.Contains("critical", StringComparison.OrdinalIgnoreCase));
            var warnings = raw.Count(x => x.Severity.Contains("تحذير", StringComparison.OrdinalIgnoreCase) || x.Severity.Contains("warning", StringComparison.OrdinalIgnoreCase));
            var score = Math.Clamp(100 - critical * 28 - warnings * 9, 0, 100); _healthScore.Text = score.ToString(); _healthScore.Foreground = score >= 90 ? B("Success") : score >= 70 ? B("Warning") : B("Danger"); _healthSummary.Text = critical > 0 ? $"{critical} حرج • {warnings} تحذير" : warnings > 0 ? $"{warnings} نقطة للمراجعة" : "لا توجد مشكلة حرجة ظاهرة."; SetStatus($"Health {score}/100 • Critical {critical} • Warnings {warnings}");
        }
        catch (Exception ex) { _findings.Children.Clear(); _findings.Children.Add(Empty("فشل التشخيص: " + ex.Message)); }
    }

    private Border Finding(D7ActionableFinding item)
    {
        var card = Card(); card.Margin = new Thickness(0, 0, 0, 8); var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var s = new StackPanel(); var t = new StackPanel { Orientation = Orientation.Horizontal }; t.Children.Add(new TextBlock { Text = item.Finding.Severity, Foreground = Severity(item.Finding.Severity), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) }); t.Children.Add(new TextBlock { Text = item.State, Foreground = B("Muted"), FontSize = 11 }); s.Children.Add(t); s.Children.Add(new TextBlock { Text = item.Finding.Title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 4) }); s.Children.Add(new TextBlock { Text = item.Finding.Detail, Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap }); if (!string.IsNullOrWhiteSpace(item.Finding.Recommendation)) s.Children.Add(new TextBlock { Text = item.Finding.Recommendation, TextWrapping = TextWrapping.Wrap, Opacity = .8, Margin = new Thickness(0, 6, 0, 0) }); Grid.SetColumn(s, 0); g.Children.Add(s);
        if (item.CanAct) { var b = Action(item.ActionLabel, async () => await FindingAction(item), item.AutomaticRepair); b.MinWidth = 150; b.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(b, 1); g.Children.Add(b); }
        card.Child = g; return card;
    }

    private async Task FindingAction(D7ActionableFinding item)
    {
        if (item.AutomaticRepair) { if (MessageBox.Show($"تنفيذ {item.ActionLabel}؟", "D7KT", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await AutoRepairAsync(item.Route); await DiagnoseAsync(false); return; }
        switch (item.Route)
        {
            case D7RepairRoute.StorageCenter: Open(new StorageCenterWindow()); break;
            case D7RepairRoute.DriverSafety: Open(new DriverSafetyWindow()); break;
            case D7RepairRoute.StartupManager: Open(new StartupManagerWindow()); break;
            case D7RepairRoute.BackgroundApps: Open(new BackgroundAppsWindow()); break;
            case D7RepairRoute.CrashInvestigator: Open(new CrashInvestigatorWindow()); break;
            case D7RepairRoute.RestoreVault: Open(new RestoreVaultWindow()); break;
            case D7RepairRoute.FanControl: Go("devices"); break;
            case D7RepairRoute.NetworkCenter: Go("devices"); await NetworkAsync(true); break;
        }
    }

    private async Task AutoRepairAsync(D7RepairRoute route) { SetStatus("تنفيذ الإصلاح…"); var r = await _actions.RunAutomaticAsync(route); Result("نتيجة الإصلاح", r); SetStatus("اكتمل الإجراء — أعد الفحص للتحقق."); }
    private async Task MissionAsync(D7Mission mission) { var r = await _missions.ApplyAsync(mission, Game()); _missionStatus.Text = r.Summary; Result("Mission", string.Join("\n\n", r.Steps.Select(x => $"{(x.Success ? "✓" : "!")} {x.Step}\n{x.Detail}")) + "\n\n" + r.Summary); }
    private Task ToggleScene() { var s = _sceneStore.Load(); s.Enabled = !s.Enabled; _sceneStore.Save(s); _sceneMessage = s.Enabled ? "Auto Scene ON." : "Auto Scene OFF."; _sceneStatus.Text = SceneText(); return Task.CompletedTask; }
    private string SceneText() { var s = _sceneStore.Load(); return $"{(s.Enabled ? "ON" : "OFF")} • Delay {s.StabilityDelaySeconds}s • {D7MissionEngine.MissionArabic(_missions.ActiveMission)} • Game {Game() ?? "—"}\n{_sceneMessage}"; }

    private async Task StartReplay() { try { _captureStatus.Text = await _shadow.StartAsync(); RegisterHotkey(); } catch (Exception ex) { _captureStatus.Text = ex.Message; } }
    private async Task SaveReplay() { try { _captureStatus.Text = await _shadow.SaveReplayAsync(); } catch (Exception ex) { _captureStatus.Text = ex.Message; } }
    private async Task StopReplay() { try { _captureStatus.Text = await _shadow.StopAsync(); } catch (Exception ex) { _captureStatus.Text = ex.Message; } }
    private async Task RefreshCapture() { var s = await _shadow.GetStatusAsync(); var x = _shadow.LoadSettings(); _captureStatus.Text = $"{s.Detail}\n{x.ReplaySeconds}s • {x.SaveHotkey} • {x.SaveFolder}"; }

    private void ToggleHud()
    {
        if (_hud != null) { try { _hud.Close(); } catch { } _hud = null; SetStatus("HUD OFF"); return; }
        var game = Game(); if (string.IsNullOrWhiteSpace(game)) { MessageBox.Show("افتح اللعبة أولًا.", "D7KT • HUD"); return; }
        _hud = new GameOverlayWindow(_hardware, game) { Owner = this }; _hud.Closed += (_, _) => _hud = null; _hud.Show(); SetStatus("HUD ON");
    }

    private async Task NetworkAsync(bool visible)
    {
        try { if (visible) _networkStatus.Text = "جاري القياس…"; var r = await _network.ScanAsync(); _networkStatus.Text = $"{r.AdapterName} • {r.IPv4}\nPing {Ms(r.InternetLatencyMs)} • Jitter {Ms(r.JitterMs)} • Loss {r.PacketLossPercent:0.#}%\n{r.Notes}"; }
        catch (Exception ex) { _networkStatus.Text = "Network: " + ex.Message; }
    }
    private async Task ApplyNetworkAsync() { if (MessageBox.Show("سيتم حفظ Backup وتطبيق خصائص NIC الآمنة. متابعة؟", "D7KT", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; var r = await _networkProfile.ApplyAsync(); _networkStatus.Text = r.Detail; await Task.Delay(5000); await NetworkAsync(true); }
    private async Task RestoreNetworkAsync() { var r = await _networkProfile.RestoreAsync(); _networkStatus.Text = r.Detail; await Task.Delay(4000); await NetworkAsync(true); }

    private async Task UpdateAsync(bool install, bool silent)
    {
        if (_updateBusy) return; _updateBusy = true;
        try
        {
            _updateProgress.Value = 0; _updateStatus.Text = "جاري التحقق…"; var u = await _updates.CheckAsync();
            if (!u.UpdateAvailable) { _updateStatus.Text = $"أحدث إصدار • {_updates.CurrentVersionText}"; return; }
            _updateStatus.Text = $"متوفر v{u.LatestVersion.ToString(3)} • إصدارك {_updates.CurrentVersionText}";
            if (!install) { if (!silent) MessageBox.Show(_updateStatus.Text, "D7KT • Update"); return; }
            if (MessageBox.Show($"تحديث D7KT إلى v{u.LatestVersion.ToString(3)}؟\nسيظهر تقدم التنزيل والتثبيت.", "D7KT • Update", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var p = new Progress<double>(v => { _updateProgress.Value = v; _updateStatus.Text = $"تنزيل… {v:0}%"; }); var installer = await _updates.DownloadAndVerifyAsync(u, p); _updateStatus.Text = "SHA-256 صحيح • تشغيل المثبت…"; D7UpdateService.LaunchInstaller(installer); await Task.Delay(1200); Application.Current.Shutdown();
        }
        catch (Exception ex) { _updateStatus.Text = "فشل التحديث: " + ex.Message; if (!silent) MessageBox.Show(_updateStatus.Text, "D7KT • Update", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _updateBusy = false; }
    }

    private void RegisterHotkey()
    {
        UnregisterHotkey(); _hwnd = new WindowInteropHelper(this).Handle; if (_hwnd == IntPtr.Zero) return; _hotkeySource = HwndSource.FromHwnd(_hwnd); _hotkeySource?.AddHook(HotkeyProc); var key = FunctionKey(_shadow.LoadSettings().SaveHotkey); if (key == 0) key = 0x77; if (!RegisterHotKey(_hwnd, HotkeyId, 0, (uint)key)) SetStatus("Hotkey الالتقاط مستخدم من برنامج آخر.");
    }
    private void UnregisterHotkey() { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId); if (_hotkeySource != null) _hotkeySource.RemoveHook(HotkeyProc); _hotkeySource = null; _hwnd = IntPtr.Zero; }
    private IntPtr HotkeyProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) { if (msg == WmHotkey && wp.ToInt32() == HotkeyId) { handled = true; _ = SaveReplay(); } return IntPtr.Zero; }
    private static int FunctionKey(string? value) { if (string.IsNullOrWhiteSpace(value)) return 0; var t = value.Trim().ToUpperInvariant(); return t.StartsWith('F') && int.TryParse(t[1..], out var n) && n is >= 1 and <= 24 ? 0x70 + n - 1 : 0; }

    private async Task ShutdownAsync()
    {
        _timer.Stop(); UnregisterHotkey(); try { _hud?.Close(); } catch { } try { _contract.Dispose(); } catch { } try { _fans.Dispose(); } catch { } try { await _sessions.DisposeAsync(); } catch { } try { await _missions.DisposeAsync(); } catch { } try { await _shadow.DisposeAsync(); } catch { } try { _hardware.Dispose(); } catch { }
    }

    private void Go(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return; _host.Content = page;
        foreach (var pair in _nav) { var on = pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase); pair.Value.Background = on ? B("AccentSoft") : Brushes.Transparent; pair.Value.BorderBrush = on ? B("Accent") : Brushes.Transparent; }
        if (key == "capture") _ = RefreshCapture(); if (key == "updates") _ = UpdateAsync(false, true);
    }

    private void Nav(StackPanel panel, string key, string glyph, string title, string sub)
    {
        var b = new Button { Tag = key, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(12, 9, 12, 9), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var i = new TextBlock { Text = glyph, FontSize = 17, Foreground = B("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(i, 0); g.Children.Add(i);
        var s = new StackPanel(); s.Children.Add(new TextBlock { Text = title, FontSize = 13.3, FontWeight = FontWeights.SemiBold }); s.Children.Add(new TextBlock { Text = sub, FontSize = 9.3, Foreground = B("Muted"), Margin = new Thickness(0, 2, 0, 0) }); Grid.SetColumn(s, 1); g.Children.Add(s); b.Content = g; b.Click += (_, _) => Go(key); panel.Children.Add(b); _nav[key] = b;
    }

    private Border Metric(string title, TextBlock value, TextBlock sub)
    {
        var c = Card(); c.Margin = new Thickness(4); var s = new StackPanel(); s.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = B("Muted"), FlowDirection = FlowDirection.LeftToRight }); value.FontSize = 28; value.FontWeight = FontWeights.Black; value.FlowDirection = FlowDirection.LeftToRight; value.Margin = new Thickness(0, 5, 0, 3); s.Children.Add(value); sub.Foreground = B("Muted"); sub.FontSize = 10.3; sub.FlowDirection = FlowDirection.LeftToRight; sub.TextWrapping = TextWrapping.Wrap; s.Children.Add(sub); c.Child = s; return c;
    }
    private Border MissionCard(string t, string d, D7Mission m) => Hero(t, d, "تشغيل", async () => await MissionAsync(m));
    private Border Hero(string t, string d, string action, Func<Task> run) { var c = Card(); c.Margin = new Thickness(4); var s = new StackPanel(); s.Children.Add(new TextBlock { Text = t, FontSize = 15.5, FontWeight = FontWeights.Bold }); s.Children.Add(new TextBlock { Text = d, Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10), MinHeight = 36 }); var b = Action(action, run, true); b.HorizontalAlignment = HorizontalAlignment.Stretch; s.Children.Add(b); c.Child = s; return c; }
    private Border Tool(string t, string d, Action open) { var c = Card(); c.Margin = new Thickness(4); c.Cursor = Cursors.Hand; var s = new StackPanel(); s.Children.Add(new TextBlock { Text = t, FontSize = 15, FontWeight = FontWeights.Bold }); s.Children.Add(new TextBlock { Text = d, Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8), MinHeight = 34 }); var b = new Button { Content = "فتح", HorizontalAlignment = HorizontalAlignment.Stretch }; b.Click += (_, _) => open(); s.Children.Add(b); c.Child = s; return c; }
    private Border Card() => new() { Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(15), Margin = new Thickness(4) };
    private StackPanel PageStack() => new() { Margin = new Thickness(8, 2, 8, 18) };
    private ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private UIElement Header(string t, string d) { var s = new StackPanel(); s.Children.Add(new TextBlock { Text = t, FontSize = 27, FontWeight = FontWeights.Bold }); s.Children.Add(new TextBlock { Text = d, Foreground = B("Muted"), FontSize = 12.3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) }); return s; }
    private UIElement Section(string t, string d) { var s = new StackPanel(); s.Children.Add(new TextBlock { Text = t, FontSize = 18, FontWeight = FontWeights.Bold }); s.Children.Add(new TextBlock { Text = d, Foreground = B("Muted"), FontSize = 11.3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }); return s; }
    private Border Empty(string text) => new() { Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22), Child = new TextBlock { Text = text, Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center } };
    private TextBlock Small(string t) => new() { Text = t, Foreground = B("Muted"), FontSize = 10.3 };
    private TextBlock BrandPart(string t, Brush color, double size) => new() { Text = t, FontSize = size, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic, Foreground = color };
    private Button TitleButton(string t, Action a, bool danger = false) { var b = new Button { Content = t, Width = 44, Height = 32, MinHeight = 32, Margin = new Thickness(2), Padding = new Thickness(0), FontSize = 17, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = danger ? B("Danger") : B("Text") }; b.Click += (_, _) => a(); return b; }
    private Button Action(string t, Func<Task> run, bool accent = false) { var b = new Button { Content = t, MinWidth = 132, Margin = new Thickness(4), Background = accent ? B("AccentStrong") : B("Panel2"), BorderBrush = accent ? B("Accent") : B("Border") }; b.Click += async (_, _) => { b.IsEnabled = false; try { await run(); } catch (Exception ex) { MessageBox.Show(ex.Message, "D7KT", MessageBoxButton.OK, MessageBoxImage.Error); } finally { b.IsEnabled = true; } }; return b; }

    private void Open(Window w) { w.Owner = this; if (w.Icon == null) w.Icon = Icon; w.ShowDialog(); }
    private string? Game() => _orchestrator.LastStatus?.Context.PrimaryGame;
    private void SetStatus(string t) => _status.Text = (t ?? string.Empty).Replace(Environment.NewLine, " • ");
    private Brush Severity(string t) => t.Contains("حرج", StringComparison.OrdinalIgnoreCase) || t.Contains("critical", StringComparison.OrdinalIgnoreCase) ? B("Danger") : t.Contains("تحذير", StringComparison.OrdinalIgnoreCase) || t.Contains("warning", StringComparison.OrdinalIgnoreCase) ? B("Warning") : t.Contains("سليم", StringComparison.OrdinalIgnoreCase) || t.Contains("ok", StringComparison.OrdinalIgnoreCase) ? B("Success") : B("Accent");
    private Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static string Ms(double? v) => v.HasValue ? $"{v.Value:0.0}ms" : "—";
    private void Result(string title, string text) { var w = new Window { Title = "D7KT • " + title, Width = 900, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = B("Bg"), Foreground = B("Text"), Owner = this, Icon = Icon }; w.Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Margin = new Thickness(14) }; w.ShowDialog(); }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
