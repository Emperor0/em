using D7SystemIntelligence.Core;
using System.Diagnostics;
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
    private const int ShadowCaptureHotkeyId = 0xD7A1;
    private const int WmHotkey = 0x0312;

    private readonly HardwareEngine _hardware = new();
    private readonly D7Orchestrator _orchestrator = new();
    private readonly LauncherScanner _launchers = new();
    private readonly DiagnosticsEngine _diagnostics = new();
    private readonly D7ActionCenterService _actionCenter = new();
    private readonly D7UpdateService _updates = new();
    private readonly NetworkIntelligence _network = new();
    private readonly NetworkGamingProfileService _networkProfile = new();
    private readonly ShadowCaptureService _shadow = new();
    private readonly FullHealthCheckService _fullHealth;
    private readonly D7MissionEngine _missions;
    private readonly GameSessionService _sessions;
    private readonly BenchmarkLabService _benchmark;
    private readonly PerformanceContractSettingsStore _contractStore = new();
    private readonly PerformanceContractService _contract;
    private readonly AutoSceneSettingsStore _autoSceneStore = new();
    private readonly AutoSceneDirector _autoScene;
    private readonly SmartFanController _smartFans;
    private readonly CodAdapter _cod = new();

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentControl _pageHost = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _mode = new();
    private readonly TextBlock _healthScore = new();
    private readonly TextBlock _healthCaption = new();
    private readonly StackPanel _repairList = new();
    private readonly TextBlock _updateState = new();
    private readonly ProgressBar _updateProgress = new();
    private readonly TextBlock _captureState = new();
    private readonly TextBlock _networkState = new();
    private readonly TextBlock _fanState = new();
    private readonly TextBlock _autoSceneState = new();
    private readonly TextBlock _missionState = new();
    private readonly TextBlock _cpuValue = new();
    private readonly TextBlock _gpuValue = new();
    private readonly TextBlock _ramValue = new();
    private readonly TextBlock _vramValue = new();
    private readonly TextBlock _cpuSub = new();
    private readonly TextBlock _gpuSub = new();
    private readonly TextBlock _bottomStats = new();

    private HardwareSnapshot? _lastHardware;
    private GameOverlayWindow? _hud;
    private HwndSource? _hotkeySource;
    private IntPtr _windowHandle;
    private bool _refreshBusy;
    private bool _sessionBusy;
    private bool _autoSceneBusy;
    private bool _updateBusy;
    private DateTimeOffset _lastNetworkScan = DateTimeOffset.MinValue;
    private string _autoSceneMessage = "Auto Scene جاهز.";

    public D7KtShellWindow()
    {
        _fullHealth = new FullHealthCheckService(_hardware);
        _missions = new D7MissionEngine(_hardware);
        _sessions = new GameSessionService(_hardware);
        _benchmark = new BenchmarkLabService(_sessions);
        _contract = new PerformanceContractService(_sessions, _hardware, _shadow);
        _autoScene = new AutoSceneDirector(_autoSceneStore);
        _smartFans = new SmartFanController(_hardware);

        Title = "D7KT • System Intelligence";
        Width = 1540;
        Height = 900;
        MinWidth = 1180;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = Brush("Bg");
        Foreground = Brush("Text");
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
        RegisterPages();
        Navigate("dashboard");

        _missions.StatusChanged += text => Dispatcher.Invoke(() =>
        {
            _missionState.Text = text;
            SetStatus(text);
        });
        _sessions.StatusChanged += text => Dispatcher.Invoke(() => SetStatus(text));
        _smartFans.StatusChanged += text => Dispatcher.Invoke(() =>
        {
            _fanState.Text = text;
            SetStatus(text);
        });
        _contract.StatusChanged += text => Dispatcher.Invoke(() =>
        {
            if (text.StartsWith("CONTRACT", StringComparison.OrdinalIgnoreCase)) SetStatus(text);
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshLiveAsync();

        Loaded += async (_, _) => await InitializeAsync();
        SourceInitialized += (_, _) => RegisterCaptureHotkey();
        Closed += async (_, _) => await DisposeAsync();
    }

    private UIElement BuildShell()
    {
        var root = new Grid { Background = Brush("Bg") };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        var titleBar = BuildTitleBar();
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var body = new Grid { Margin = new Thickness(12, 0, 12, 8) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nav = BuildNavigation();
        Grid.SetColumn(nav, 0);
        body.Children.Add(nav);

        _pageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _pageHost.VerticalContentAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(_pageHost, 2);
        body.Children.Add(_pageHost);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Border
        {
            Background = Brush("Panel"),
            BorderBrush = Brush("Border"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 4)
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Text = "D7KT Engine يبدأ…";
        _status.Foreground = Brush("Muted");
        _status.FontSize = 11.5;
        _bottomStats.Foreground = Brush("Muted");
        _bottomStats.FontSize = 11.5;
        _bottomStats.FlowDirection = FlowDirection.LeftToRight;
        Grid.SetColumn(_status, 0);
        Grid.SetColumn(_bottomStats, 1);
        footerGrid.Children.Add(_status);
        footerGrid.Children.Add(_bottomStats);
        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Border
        {
            Background = Brush("Panel"),
            BorderBrush = Brush("Border"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
        brand.Children.Add(new TextBlock
        {
            Text = "D",
            FontSize = 22,
            FontWeight = FontWeights.Black,
            Foreground = Brush("Text")
        });
        brand.Children.Add(new TextBlock
        {
            Text = "7",
            FontSize = 22,
            FontWeight = FontWeights.Black,
            Foreground = Brush("Accent")
        });
        brand.Children.Add(new TextBlock
        {
            Text = "KT",
            FontSize = 22,
            FontWeight = FontWeights.Black,
            Foreground = Brush("Text")
        });
        brand.Children.Add(new TextBlock
        {
            Text = "  SYSTEM INTELLIGENCE",
            FontSize = 10,
            Foreground = Brush("Muted"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 2, 0, 0)
        });
        Grid.SetColumn(brand, 0);
        grid.Children.Add(brand);

        _mode.Text = "الوضع الطبيعي";
        _mode.FontWeight = FontWeights.SemiBold;
        _mode.Foreground = Brush("Muted");
        _mode.VerticalAlignment = VerticalAlignment.Center;
        _mode.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_mode, 1);
        grid.Children.Add(_mode);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        controls.Children.Add(TitleButton("—", () => WindowState = WindowState.Minimized));
        controls.Children.Add(TitleButton("□", ToggleMaximize));
        controls.Children.Add(TitleButton("×", Close, true));
        Grid.SetColumn(controls, 2);
        grid.Children.Add(controls);

        bar.Child = grid;
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
        };
        return bar;
    }

    private Button TitleButton(string text, Action action, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Width = 44,
            Height = 32,
            MinHeight = 32,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            FontSize = 17,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = danger ? Brush("Danger") : Brush("Text")
        };
        button.Click += (_, _) => action();
        return button;
    }

    private UIElement BuildNavigation()
    {
        var border = new Border
        {
            Background = Brush("Panel"),
            BorderBrush = Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(10)
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(34, 5, 7), Color.FromRgb(12, 12, 15), 90),
            BorderBrush = Brush("AccentSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var logoStack = new StackPanel { FlowDirection = FlowDirection.LeftToRight };
        var logoText = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        logoText.Children.Add(LogoPart("D", Brush("Text")));
        logoText.Children.Add(LogoPart("7", Brush("Accent")));
        logoText.Children.Add(LogoPart("KT", Brush("Text")));
        logoStack.Children.Add(logoText);
        logoStack.Children.Add(new TextBlock { Text = "SYSTEM INTELLIGENCE • 2026", FontSize = 9, Foreground = Brush("Muted"), Margin = new Thickness(2, 3, 0, 0) });
        logo.Child = logoStack;
        Grid.SetRow(logo, 0);
        root.Children.Add(logo);

        var nav = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        AddNav(nav, "dashboard", "⌂", "لوحة التحكم", "نظرة واحدة لكل شيء");
        AddNav(nav, "health", "✚", "التشخيص والإصلاح", "اكتشاف + إجراء فعلي");
        AddNav(nav, "gaming", "◈", "الألعاب والأداء", "Missions وAuto Scene");
        AddNav(nav, "devices", "⌁", "الأجهزة والنظام", "Display • RGB • Audio • Drivers");
        AddNav(nav, "capture", "◉", "التقاط وبث", "Replay • HUD • Stream");
        AddNav(nav, "updates", "↻", "التحديثات والإعدادات", "D7 + Windows + Apps");
        Grid.SetRow(nav, 1);
        root.Children.Add(nav);

        var versionCard = new Border
        {
            Background = Brush("Panel2"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 0)
        };
        versionCard.Child = new TextBlock
        {
            Text = $"D7KT {_updates.CurrentVersionText}\n● المحرك المحلي",
            Foreground = Brush("Muted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(versionCard, 2);
        root.Children.Add(versionCard);

        border.Child = root;
        return border;
    }

    private TextBlock LogoPart(string text, Brush color) => new()
    {
        Text = text,
        FontSize = 27,
        FontWeight = FontWeights.Black,
        FontStyle = FontStyles.Italic,
        Foreground = color
    };

    private void AddNav(StackPanel panel, string key, string icon, string title, string subtitle)
    {
        var button = new Button
        {
            Tag = key,
            Margin = new Thickness(0, 3, 0, 3),
            Padding = new Thickness(12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = new TextBlock
        {
            Text = icon,
            FontSize = 18,
            Foreground = Brush("Accent"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(glyph, 0);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = subtitle, FontSize = 9.5, Foreground = Brush("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(text);
        button.Content = grid;
        button.Click += (_, _) => Navigate(key);
        panel.Children.Add(button);
        _navButtons[key] = button;
    }

    private void RegisterPages()
    {
        _pages["dashboard"] = BuildDashboardPage();
        _pages["health"] = BuildHealthPage();
        _pages["gaming"] = BuildGamingPage();
        _pages["devices"] = BuildDevicesPage();
        _pages["capture"] = BuildCapturePage();
        _pages["updates"] = BuildUpdatesPage();
    }

    private UIElement BuildDashboardPage()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(8, 2, 8, 18) };

        var hero = new Border
        {
            Height = 178,
            Background = D7KtBrand.HeroBrush(),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 140, 20, 28)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(26),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var heroGrid = new Grid();
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        brand.Children.Add(new TextBlock { Text = "D", FontSize = 47, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic });
        brand.Children.Add(new TextBlock { Text = "7", FontSize = 47, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic, Foreground = Brush("Accent") });
        brand.Children.Add(new TextBlock { Text = "KT", FontSize = 47, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic });
        heroText.Children.Add(brand);
        heroText.Children.Add(new TextBlock { Text = "SYSTEM INTELLIGENCE", FontSize = 12, Foreground = Brush("Muted"), FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(4, -4, 0, 8) });
        heroText.Children.Add(new TextBlock
        {
            Text = "مركز واحد للأداء، التشخيص، الإصلاح، الألعاب، البث، الأجهزة والتحديثات — بدون Tweaks وهمية.",
            FontSize = 14,
            Foreground = Brush("Text"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 680
        });
        Grid.SetColumn(heroText, 0);
        heroGrid.Children.Add(heroText);

        var heroActions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heroActions.Children.Add(ActionButton("تشغيل فحص شامل", async () => await RunDiagnosticsAsync(true), true));
        heroActions.Children.Add(ActionButton("تشغيل PRO RANKED", async () => await ApplyMissionAsync(D7Mission.ProRanked)));
        Grid.SetColumn(heroActions, 1);
        heroGrid.Children.Add(heroActions);
        hero.Child = heroGrid;
        root.Children.Add(hero);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 0, -4, 12) };
        metrics.Children.Add(MetricCard("CPU", _cpuValue, _cpuSub));
        metrics.Children.Add(MetricCard("GPU", _gpuValue, _gpuSub));
        metrics.Children.Add(MetricCard("RAM", _ramValue, new TextBlock { Text = "ضغط الذاكرة المباشر", Foreground = Brush("Muted"), FontSize = 11 }));
        metrics.Children.Add(MetricCard("VRAM", _vramValue, new TextBlock { Text = "ذاكرة كرت الشاشة", Foreground = Brush("Muted"), FontSize = 11 }));
        root.Children.Add(metrics);

        var center = new Grid { Margin = new Thickness(-4, 0, -4, 12) };
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.9, GridUnitType.Star) });

        var health = Card();
        var healthStack = new StackPanel();
        healthStack.Children.Add(SectionTitle("التشخيص الذكي", "يعرض المشكلة وما إذا كان D7 يقدر يصلحها أو يفتح مركزها الصحيح."));
        var scoreRow = new Grid { Margin = new Thickness(0, 14, 0, 8) };
        scoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _healthScore.Text = "—";
        _healthScore.FontSize = 46;
        _healthScore.FontWeight = FontWeights.Black;
        _healthScore.Foreground = Brush("Accent");
        _healthScore.FlowDirection = FlowDirection.LeftToRight;
        Grid.SetColumn(_healthScore, 0);
        _healthCaption.Text = "لم يتم الفحص بعد";
        _healthCaption.Foreground = Brush("Muted");
        _healthCaption.TextWrapping = TextWrapping.Wrap;
        _healthCaption.VerticalAlignment = VerticalAlignment.Center;
        _healthCaption.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(_healthCaption, 1);
        scoreRow.Children.Add(_healthScore);
        scoreRow.Children.Add(_healthCaption);
        healthStack.Children.Add(scoreRow);
        healthStack.Children.Add(ActionButton("فتح مركز الإصلاح", () => { Navigate("health"); return Task.CompletedTask; }, true));
        health.Child = healthStack;
        Grid.SetColumn(health, 0);
        center.Children.Add(health);

        var mission = Card();
        var missionStack = new StackPanel();
        missionStack.Children.Add(SectionTitle("الوضع الذكي", "Mission Engine ينسق Power + Display + Network + Fans + Replay مع Restore."));
        _missionState.Text = "لا توجد Mission نشطة.";
        _missionState.Foreground = Brush("Muted");
        _missionState.TextWrapping = TextWrapping.Wrap;
        _missionState.Margin = new Thickness(0, 14, 0, 10);
        missionStack.Children.Add(_missionState);
        missionStack.Children.Add(ActionButton("فتح الألعاب والأداء", () => { Navigate("gaming"); return Task.CompletedTask; }));
        mission.Child = missionStack;
        Grid.SetColumn(mission, 1);
        center.Children.Add(mission);
        root.Children.Add(center);

        var quick = new UniformGrid { Columns = 4, Margin = new Thickness(-4) };
        quick.Children.Add(QuickCard("التقاط المقاطع", "Replay خفيف • Hotkey • مجلد تختاره", () => Navigate("capture")));
        quick.Children.Add(QuickCard("الأجهزة", "Display • RGB • Audio • Input", () => Navigate("devices")));
        quick.Children.Add(QuickCard("التحديثات", "زر واحد لتحديث D7 من داخله", () => Navigate("updates")));
        quick.Children.Add(QuickCard("Restore Vault", "رجوع آمن بدل تعديلات عمياء", () => OpenDialog(new RestoreVaultWindow())));
        root.Children.Add(quick);

        scroll.Content = root;
        return scroll;
    }

    private UIElement BuildHealthPage()
    {
        var root = new Grid { Margin = new Thickness(8, 2, 8, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = HeaderBlock("التشخيص والإصلاح", "هنا ما فيه Finding ميت: كل مشكلة توضح هل D7 يصلحها مباشرة، يفتح أداة آمنة، أو يمنع الإصلاح الآلي لأنه غير آمن.");
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var actions = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 14, -4, 12) };
        actions.Children.Add(RepairHero("فحص ذكي", "Hardware + Event Logs + RAM/VRAM + Storage signals", "فحص الآن", async () => await RunDiagnosticsAsync(true)));
        actions.Children.Add(RepairHero("Windows Repair", "DISM RestoreHealth + SFC /scannow", "إصلاح فعلي", async () => await RunAutomaticRepairAsync(D7RepairRoute.WindowsRepair)));
        actions.Children.Add(RepairHero("تنظيف آمن", "Temp أقدم من 24 ساعة فقط", "تنظيف", async () => await RunAutomaticRepairAsync(D7RepairRoute.TempCleanup)));
        actions.Children.Add(RepairHero("Full Health", "Storage + Stability + Windows Integrity", "فتح الفحص", () => { OpenDialog(new FullHealthWindow(_fullHealth)); return Task.CompletedTask; }));
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _repairList.Children.Clear();
        _repairList.Children.Add(EmptyState("اضغط «فحص الآن». بعدها كل نتيجة تظهر معها حالة واضحة وإجراء مناسب."));
        scroll.Content = _repairList;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        return root;
    }

    private UIElement BuildGamingPage()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(8, 2, 8, 18) };
        root.Children.Add(HeaderBlock("الألعاب والأداء", "بدل عشرات الـTweaks: اختر هدفًا، وD7 يطبق خطوات قابلة للقياس والرجوع."));

        var missions = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 14, -4, 12) };
        missions.Children.Add(MissionCard("PRO RANKED", "أقل latency ممكن: Power + أعلى Hz + Gaming Network + خلفية آمنة.", D7Mission.ProRanked));
        missions.Children.Add(MissionCard("STREAM + RANKED", "موازنة اللعب والبث بدون تشغيل Recorder ثاني.", D7Mission.StreamRanked));
        missions.Children.Add(MissionCard("RECORDING", "Shadow Capture + Power + Fans حسب الدعم.", D7Mission.Recording));
        missions.Children.Add(MissionCard("STORY / ULTRA", "أعلى Hz وتبريد ذكي للألعاب القصصية.", D7Mission.Story));
        missions.Children.Add(MissionCard("SILENT", "Balanced + استعادة Fan overrides.", D7Mission.Silent));
        missions.Children.Add(RepairHero("RESTORE", "إيقاف أي Mission وإرجاع الإعدادات المحفوظة.", "استعادة", async () => await RestoreMissionAsync()));
        root.Children.Add(missions);

        var autoCard = Card();
        var autoGrid = new Grid();
        autoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        autoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var autoText = new StackPanel();
        autoText.Children.Add(SectionTitle("Auto Scene", "يفرق تلقائيًا بين رانك، قصة، وبث بعد مهلة ثبات حتى لا يغيّر النظام وسط إقلاع اللعبة."));
        _autoSceneState.Text = BuildAutoSceneStatus();
        _autoSceneState.Foreground = Brush("Muted");
        _autoSceneState.TextWrapping = TextWrapping.Wrap;
        _autoSceneState.Margin = new Thickness(0, 10, 0, 0);
        autoText.Children.Add(_autoSceneState);
        Grid.SetColumn(autoText, 0);
        autoGrid.Children.Add(autoText);
        var autoButtons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        autoButtons.Children.Add(ActionButton("تشغيل / إيقاف", ToggleAutoSceneAsync, true));
        autoButtons.Children.Add(ActionButton("إعدادات Auto Scene", () => { OpenDialog(new AutoSceneWindow(_autoScene, BuildAutoSceneStatus)); return Task.CompletedTask; }));
        Grid.SetColumn(autoButtons, 1);
        autoGrid.Children.Add(autoButtons);
        autoCard.Child = autoGrid;
        root.Children.Add(autoCard);

        var labs = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 12, -4, 0) };
        labs.Children.Add(ToolCard("Mission Control", "تفاصيل كل خطوة وRestore", () => OpenDialog(new MissionControlWindow(_missions, CurrentGame))));
        labs.Children.Add(ToolCard("Performance Contract", "هدف FPS/حرارة/ضغط مع Guard", () => OpenDialog(new PerformanceContractWindow(_contract, _contractStore))));
        labs.Children.Add(ToolCard("Benchmark Lab", "Baseline → Change → Compare", () => OpenDialog(new BenchmarkLabWindow(_benchmark))));
        labs.Children.Add(ToolCard("Stutter Black Box", "FPS • 1% • P99 • أحداث التقطيع", () => OpenDialog(new SessionHistoryWindow(_sessions))));
        root.Children.Add(labs);

        var codCard = Card();
        var codStack = new StackPanel();
        codStack.Children.Add(SectionTitle("Call of Duty Adapter", "يعدل فقط مفاتيح Schema المعروفة مع Backup ويرفض التعديل إذا اللعبة شغالة."));
        var codButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        codButtons.Children.Add(ActionButton("فحص إعدادات COD", () => { var r = _cod.Locate(); MessageBox.Show(r.Message + "\n" + (r.Path ?? ""), "D7KT • COD"); return Task.CompletedTask; }));
        codButtons.Children.Add(ActionButton("Competitive", () => { MessageBox.Show(_cod.Optimize(Math.Max(1, Environment.ProcessorCount / 2), 8f, "Balanced"), "D7KT • COD"); return Task.CompletedTask; }, true));
        codButtons.Children.Add(ActionButton("Quality / 165Hz", () => { MessageBox.Show(_cod.Optimize(Math.Max(1, Environment.ProcessorCount / 2), 8f, "Quality"), "D7KT • COD"); return Task.CompletedTask; }));
        codStack.Children.Add(codButtons);
        codCard.Child = codStack;
        root.Children.Add(codCard);

        scroll.Content = root;
        return scroll;
    }

    private UIElement BuildDevicesPage()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(8, 2, 8, 18) };
        root.Children.Add(HeaderBlock("الأجهزة والنظام", "كل المختبرات موجودة، لكن مجمعة حسب الشيء الذي تريد تديره بدل قائمة طويلة متناثرة."));

        var hardware = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 14, -4, 10) };
        hardware.Children.Add(ToolCard("الشاشة", "Hz • أوضاع Display • Apply/Restore", () => OpenDialog(new DisplayControlWindow())));
        hardware.Children.Add(ToolCard("RGB Studio", "OpenRGB للأجهزة المدعومة فقط", () => OpenDialog(new RgbStudioWindow(_hardware))));
        hardware.Children.Add(ToolCard("Audio Studio", "الأجهزة الفعلية + Sonar/Virtual routing", () => OpenDialog(new AudioStudioWindow())));
        hardware.Children.Add(ToolCard("Input Lab", "Polling • Drift • Controller/HID", () => OpenDialog(new InputLabWindow())));
        hardware.Children.Add(ToolCard("Driver Safety", "Backup • فحص • مسار Rollback", () => OpenDialog(new DriverSafetyWindow())));
        hardware.Children.Add(ToolCard("Storage Center", "Health • Reliability • Analyze/ReTrim", () => OpenDialog(new StorageCenterWindow())));
        hardware.Children.Add(ToolCard("Startup Manager", "تعطيل/إدارة بدل حذف عشوائي", () => OpenDialog(new StartupManagerWindow())));
        hardware.Children.Add(ToolCard("Background Apps", "تصنيف + تنظيف ذكي", () => OpenDialog(new BackgroundAppsWindow())));
        hardware.Children.Add(ToolCard("Smart Removal", "حذف البرامج من الجذور بمرحلة فحص", () => OpenDialog(new SmartRemovalWindow())));
        hardware.Children.Add(ToolCard("Crash Investigator", "WHEA • GPU • Storage • Shutdown", () => OpenDialog(new CrashInvestigatorWindow())));
        hardware.Children.Add(ToolCard("Restore Vault", "نسخ واستعادة تغييرات D7", () => OpenDialog(new RestoreVaultWindow())));
        hardware.Children.Add(ToolCard("Full Health", "تجميع صحة الجهاز في تقرير واحد", () => OpenDialog(new FullHealthWindow(_fullHealth))));
        root.Children.Add(hardware);

        var fans = Card();
        var fanGrid = new Grid();
        fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fanText = new StackPanel();
        fanText.Children.Add(SectionTitle("Smart Fans", "D7 لن يكتب PWM إلا لقناة يثبت أنها writable. إذا جهازك Read-only سيقولها بوضوح."));
        _fanState.Text = "جاري قراءة قنوات المراوح…";
        _fanState.Foreground = Brush("Muted");
        _fanState.Margin = new Thickness(0, 10, 0, 0);
        fanText.Children.Add(_fanState);
        Grid.SetColumn(fanText, 0);
        fanGrid.Children.Add(fanText);
        var fanButtons = new StackPanel();
        fanButtons.Children.Add(ActionButton("تشغيل AUTO", () => { var ok = _smartFans.Start(); _fanState.Text = ok ? "AUTO Fan يعمل." : "لا توجد قناة writable آمنة أو تعذر بدء AUTO."; return Task.CompletedTask; }, true));
        fanButtons.Children.Add(ActionButton("إيقاف + استعادة", () => { _smartFans.Stop(true); _fanState.Text = "تمت الاستعادة إلى BIOS/AUTO."; return Task.CompletedTask; }));
        Grid.SetColumn(fanButtons, 1);
        fanGrid.Children.Add(fanButtons);
        fans.Child = fanGrid;
        root.Children.Add(fans);

        var network = Card();
        var netStack = new StackPanel();
        netStack.Children.Add(SectionTitle("Network Intelligence", "Ping/Jitter/Loss أولًا، ثم Gaming Network بتغييرات NIC مع Backup وRestore."));
        _networkState.Text = "لم يتم فحص الشبكة بعد.";
        _networkState.Foreground = Brush("Muted");
        _networkState.TextWrapping = TextWrapping.Wrap;
        _networkState.Margin = new Thickness(0, 10, 0, 10);
        netStack.Children.Add(_networkState);
        var netButtons = new StackPanel { Orientation = Orientation.Horizontal };
        netButtons.Children.Add(ActionButton("قياس الآن", ScanNetworkAsync));
        netButtons.Children.Add(ActionButton("Gaming Network", ApplyGamingNetworkAsync, true));
        netButtons.Children.Add(ActionButton("استعادة الشبكة", RestoreGamingNetworkAsync));
        netStack.Children.Add(netButtons);
        network.Child = netStack;
        root.Children.Add(network);

        scroll.Content = root;
        return scroll;
    }

    private UIElement BuildCapturePage()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(8, 2, 8, 18) };
        root.Children.Add(HeaderBlock("التقاط وبث", "Replay واحد وخفيف: D7 يستفيد من OBS Replay Buffer بدل تشغيل Encoder/Recorder ثاني بلا داعي."));

        var capture = Card();
        var capStack = new StackPanel();
        capStack.Children.Add(SectionTitle("D7 Shadow Capture", "تحدد المدة والمجلد والHotkey. الضغط على Hotkey يحفظ آخر المدة فقط."));
        _captureState.Text = "جاري قراءة Replay Buffer…";
        _captureState.Foreground = Brush("Muted");
        _captureState.TextWrapping = TextWrapping.Wrap;
        _captureState.Margin = new Thickness(0, 10, 0, 12);
        capStack.Children.Add(_captureState);
        var capButtons = new WrapPanel();
        capButtons.Children.Add(ActionButton("تشغيل Replay", StartCaptureAsync, true));
        capButtons.Children.Add(ActionButton("حفظ آخر مقطع", SaveReplayAsync, true));
        capButtons.Children.Add(ActionButton("إيقاف Replay", StopCaptureAsync));
        capButtons.Children.Add(ActionButton("الإعدادات", () => { OpenDialog(new ShadowCaptureWindow(_shadow)); RegisterCaptureHotkey(); return Task.CompletedTask; }));
        capButtons.Children.Add(ActionButton("مكتبة المقاطع", () => { OpenDialog(new ClipLibraryWindow(() => _shadow.LoadSettings().SaveFolder)); return Task.CompletedTask; }));
        capStack.Children.Add(capButtons);
        capture.Child = capStack;
        root.Children.Add(capture);

        var tools = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 12, -4, 0) };
        tools.Children.Add(ToolCard("D7 HUD", "FPS • 1% • Frametime • Temps • Ping", ToggleHud));
        tools.Children.Add(ToolCard("Stream Director", "OBS/TikTok + أولويات + جلسة اللعبة", () => OpenDialog(new StreamDirectorWindow(CurrentGame()))));
        tools.Children.Add(ToolCard("Session History", "تقارير جلسات اللعب وStutters", () => OpenDialog(new SessionHistoryWindow(_sessions))));
        root.Children.Add(tools);

        scroll.Content = root;
        return scroll;
    }

    private UIElement BuildUpdatesPage()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(8, 2, 8, 18) };
        root.Children.Add(HeaderBlock("التحديثات والإعدادات", "هذه هي القاعدة: تحديث D7 من داخل D7. تنزيل → SHA-256 → تشغيل المثبت → إعادة فتح البرنامج."));

        var updater = Card();
        var upStack = new StackPanel();
        upStack.Children.Add(SectionTitle("D7KT Self Update", "لن تحتاج تحمل Installer يدويًا بعد الآن. وإذا فشل شيء يظهر السبب بدل الصمت."));
        _updateState.Text = $"الإصدار الحالي: {_updates.CurrentVersionText}";
        _updateState.Foreground = Brush("Muted");
        _updateState.Margin = new Thickness(0, 10, 0, 8);
        _updateState.TextWrapping = TextWrapping.Wrap;
        upStack.Children.Add(_updateState);
        _updateProgress.Minimum = 0;
        _updateProgress.Maximum = 100;
        _updateProgress.Value = 0;
        _updateProgress.Height = 7;
        _updateProgress.Margin = new Thickness(0, 0, 0, 10);
        upStack.Children.Add(_updateProgress);
        var upButtons = new WrapPanel();
        upButtons.Children.Add(ActionButton("فحص وتحديث D7 الآن", () => CheckAndInstallUpdateAsync(true), true));
        upButtons.Children.Add(ActionButton("فحص فقط", () => CheckAndInstallUpdateAsync(false)));
        upStack.Children.Add(upButtons);
        updater.Child = upStack;
        root.Children.Add(updater);

        var system = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 12, -4, 0) };
        system.Children.Add(RepairHero("تحديث التطبيقات", "Winget upgrade --all", "تحديث", async () => ShowLongResult("D7KT • Apps Update", await SystemActions.UpgradeAppsAsync())));
        system.Children.Add(RepairHero("Windows Integrity", "DISM ScanHealth + SFC VerifyOnly", "فحص", async () => ShowLongResult("D7KT • Windows Scan", await SystemActions.RunWindowsRepairScanAsync())));
        system.Children.Add(RepairHero("Windows Repair", "RestoreHealth + SFC /scannow", "إصلاح", async () => await RunAutomaticRepairAsync(D7RepairRoute.WindowsRepair)));
        system.Children.Add(RepairHero("فحص القرص", "CHKDSK /scan بدون Offline repair", "فحص", async () => ShowLongResult("D7KT • Disk Check", await SystemActions.CheckSystemDriveAsync())));
        system.Children.Add(RepairHero("Flush DNS", "إصلاح Cache DNS فقط — ليس Tweaker للبنق", "تنفيذ", async () => ShowLongResult("D7KT • DNS", await SystemActions.FlushDnsAsync())));
        system.Children.Add(RepairHero("Restore Vault", "راجع النسخ والرجوع للتغييرات", "فتح", () => { OpenDialog(new RestoreVaultWindow()); return Task.CompletedTask; }));
        root.Children.Add(system);

        scroll.Content = root;
        return scroll;
    }

    private async Task InitializeAsync()
    {
        SetStatus("جاري تشغيل D7KT Engine…");
        try
        {
            var games = await _launchers.ScanAsync();
            _orchestrator.SetKnownGames(games);
            SetStatus($"D7KT جاهز • تم التعرف على {games.Count} تثبيت لعبة/منصة");
        }
        catch (Exception ex) { SetStatus("Launcher scan: " + ex.Message); }

        var savedContract = _contractStore.Load();
        if (savedContract.Enabled) _contract.Start(savedContract);

        _timer.Start();
        await RefreshLiveAsync();
        _ = CheckAndInstallUpdateAsync(false, silent: true);
        _ = RefreshCaptureStatusAsync();
    }

    private async Task RefreshLiveAsync()
    {
        if (_refreshBusy) return;
        _refreshBusy = true;
        try
        {
            var hw = _hardware.Read();
            _lastHardware = hw;
            _cpuValue.Text = $"{hw.CpuLoad:0}%";
            _gpuValue.Text = $"{hw.GpuLoad:0}%";
            _ramValue.Text = $"{hw.RamLoad:0}%";
            _vramValue.Text = hw.VramLoad.HasValue ? $"{hw.VramLoad:0}%" : "—";
            _cpuSub.Text = $"{hw.CpuTemp:0}°C • {hw.CpuName}";
            _gpuSub.Text = $"{hw.GpuTemp:0}°C • {hw.GpuName}";
            _fanState.Text = hw.Fans.Count == 0
                ? "لم تظهر قنوات مراوح من Hardware Monitor."
                : $"قنوات مرصودة {hw.Fans.Count} • قابلة للكتابة {hw.Fans.Count(x => x.Controllable)}";

            var live = await _orchestrator.ObserveAsync(hw);
            _mode.Text = $"{D7Orchestrator.ModeArabic(live.Context.Mode)} • {D7MissionEngine.MissionArabic(_missions.ActiveMission)}";
            _missionState.Text = live.Summary;
            _bottomStats.Text = $"CPU {hw.CpuLoad:0}%  •  GPU {hw.GpuLoad:0}%  •  RAM {hw.RamLoad:0}%  •  {DateTime.Now:HH:mm:ss}";

            await SyncGameSessionAsync(live.Context.PrimaryGame);
            await EvaluateAutoSceneAsync(live.Context);

            if ((DateTimeOffset.Now - _lastNetworkScan).TotalSeconds >= 20)
            {
                _lastNetworkScan = DateTimeOffset.Now;
                _ = ScanNetworkAsync(false);
            }
        }
        catch (Exception ex) { SetStatus("Live Engine: " + ex.Message); }
        finally { _refreshBusy = false; }
    }

    private async Task SyncGameSessionAsync(string? game)
    {
        if (_sessionBusy) return;
        _sessionBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(game))
            {
                if (_sessions.IsRunning) await _sessions.StopAsync();
                return;
            }
            if (_sessions.IsRunning && string.Equals(_sessions.ActiveGame, game, StringComparison.OrdinalIgnoreCase)) return;
            if (_sessions.IsRunning) await _sessions.StopAsync();
            await _sessions.StartAsync(game);
        }
        catch (Exception ex) { SetStatus("Stutter Black Box: " + ex.Message); }
        finally { _sessionBusy = false; }
    }

    private async Task EvaluateAutoSceneAsync(RuntimeContext context)
    {
        if (_autoSceneBusy) return;
        _autoSceneBusy = true;
        try
        {
            var evaluation = _autoScene.Evaluate(context, _missions.ActiveMission);
            _autoSceneMessage = evaluation.Reason;
            _autoSceneState.Text = BuildAutoSceneStatus();
            if (!evaluation.Ready) return;

            if (evaluation.Target == D7Mission.None)
                await _missions.RestoreAsync();
            else
                await _missions.ApplyAsync(evaluation.Target, context.PrimaryGame);
        }
        catch (Exception ex) { _autoSceneMessage = "Auto Scene: " + ex.Message; }
        finally { _autoSceneBusy = false; }
    }

    private async Task RunDiagnosticsAsync(bool navigate)
    {
        if (navigate) Navigate("health");
        _repairList.Children.Clear();
        _repairList.Children.Add(EmptyState("جاري فحص الجهاز والأحداث…"));
        try
        {
            var hw = _lastHardware ?? _hardware.Read();
            var findings = await _diagnostics.RunAsync(hw);
            var actionable = _actionCenter.Classify(findings).ToList();
            actionable.Insert(0, D7ActionCenterService.WindowsRepairCard());
            actionable.Insert(1, D7ActionCenterService.TempCleanupCard());

            _repairList.Children.Clear();
            foreach (var item in actionable)
                _repairList.Children.Add(FindingCard(item));

            var critical = findings.Count(x => x.Severity.Contains("حرج", StringComparison.OrdinalIgnoreCase) || x.Severity.Contains("critical", StringComparison.OrdinalIgnoreCase));
            var warnings = findings.Count(x => x.Severity.Contains("تحذير", StringComparison.OrdinalIgnoreCase) || x.Severity.Contains("warning", StringComparison.OrdinalIgnoreCase));
            var score = Math.Clamp(100 - critical * 28 - warnings * 9, 0, 100);
            _healthScore.Text = score.ToString();
            _healthScore.Foreground = score >= 90 ? Brush("Success") : score >= 70 ? Brush("Warning") : Brush("Danger");
            _healthCaption.Text = critical > 0
                ? $"{critical} مشكلة حرجة • {warnings} تحذير. راجع الإجراءات أدناه."
                : warnings > 0 ? $"{warnings} نقطة تحتاج مراجعة. لا يوجد إصلاح عشوائي." : "لا توجد مشكلة حرجة ظاهرة في الفحص الحالي.";
            SetStatus($"التشخيص اكتمل • Health {score}/100 • Critical {critical} • Warnings {warnings}");
        }
        catch (Exception ex)
        {
            _repairList.Children.Clear();
            _repairList.Children.Add(EmptyState("فشل التشخيص: " + ex.Message));
        }
    }

    private Border FindingCard(D7ActionableFinding item)
    {
        var border = Card();
        border.Margin = new Thickness(0, 0, 0, 9);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock
        {
            Text = item.Finding.Severity,
            Foreground = SeverityBrush(item.Finding.Severity),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 10, 0)
        });
        top.Children.Add(new TextBlock { Text = item.State, Foreground = Brush("Muted"), FontSize = 11 });
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock { Text = item.Finding.Title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 4) });
        stack.Children.Add(new TextBlock { Text = item.Finding.Detail, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(item.Finding.Recommendation))
            stack.Children.Add(new TextBlock { Text = item.Finding.Recommendation, Foreground = Brush("Text"), Opacity = .82, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Grid.SetColumn(stack, 0);
        grid.Children.Add(stack);

        if (item.CanAct)
        {
            var button = new Button { Content = item.ActionLabel, MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
            if (item.AutomaticRepair) button.Background = Brush("AccentStrong");
            button.Click += async (_, _) => await ExecuteFindingActionAsync(item);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }
        border.Child = grid;
        return border;
    }

    private async Task ExecuteFindingActionAsync(D7ActionableFinding item)
    {
        if (item.AutomaticRepair)
        {
            if (MessageBox.Show($"تنفيذ: {item.ActionLabel}؟\n\n{item.Finding.Detail}", "D7KT • Action Center", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            await RunAutomaticRepairAsync(item.Route);
            await RunDiagnosticsAsync(false);
            return;
        }

        switch (item.Route)
        {
            case D7RepairRoute.StorageCenter: OpenDialog(new StorageCenterWindow()); break;
            case D7RepairRoute.DriverSafety: OpenDialog(new DriverSafetyWindow()); break;
            case D7RepairRoute.StartupManager: OpenDialog(new StartupManagerWindow()); break;
            case D7RepairRoute.BackgroundApps: OpenDialog(new BackgroundAppsWindow()); break;
            case D7RepairRoute.CrashInvestigator: OpenDialog(new CrashInvestigatorWindow()); break;
            case D7RepairRoute.RestoreVault: OpenDialog(new RestoreVaultWindow()); break;
            case D7RepairRoute.FanControl: Navigate("devices"); break;
            case D7RepairRoute.NetworkCenter: Navigate("devices"); await ScanNetworkAsync(); break;
        }
    }

    private async Task RunAutomaticRepairAsync(D7RepairRoute route)
    {
        SetStatus("D7KT ينفذ الإصلاح الآن…");
        var result = await _actionCenter.RunAutomaticAsync(route);
        ShowLongResult("D7KT • نتيجة الإصلاح", result);
        SetStatus("اكتمل الإجراء. شغّل التشخيص مرة أخرى للتحقق.");
    }

    private async Task ApplyMissionAsync(D7Mission mission)
    {
        SetStatus($"تطبيق {D7MissionEngine.MissionArabic(mission)}…");
        var result = await _missions.ApplyAsync(mission, CurrentGame());
        _missionState.Text = result.Summary;
        MessageBox.Show(string.Join("\n\n", result.Steps.Select(x => $"{(x.Success ? "✓" : "!")} {x.Step}\n{x.Detail}")) + "\n\n" + result.Summary,
            "D7KT • Mission", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RestoreMissionAsync()
    {
        var result = await _missions.RestoreAsync();
        _missionState.Text = result.Summary;
        SetStatus(result.Summary);
    }

    private Task ToggleAutoSceneAsync()
    {
        var settings = _autoSceneStore.Load();
        settings.Enabled = !settings.Enabled;
        _autoSceneStore.Save(settings);
        _autoSceneMessage = settings.Enabled ? "Auto Scene ON — ينتظر لعبة مستقرة قبل تطبيق Mission." : "Auto Scene OFF.";
        _autoSceneState.Text = BuildAutoSceneStatus();
        SetStatus(_autoSceneMessage);
        return Task.CompletedTask;
    }

    private string BuildAutoSceneStatus()
    {
        var settings = _autoSceneStore.Load();
        return $"{(settings.Enabled ? "ON" : "OFF")} • Delay {settings.StabilityDelaySeconds}s • Mission {D7MissionEngine.MissionArabic(_missions.ActiveMission)} • Game {CurrentGame() ?? "—"}\n{_autoSceneMessage}";
    }

    private async Task StartCaptureAsync()
    {
        try { _captureState.Text = await _shadow.StartAsync(); RegisterCaptureHotkey(); }
        catch (Exception ex) { _captureState.Text = "Shadow Capture: " + ex.Message; }
    }

    private async Task SaveReplayAsync()
    {
        try { _captureState.Text = await _shadow.SaveReplayAsync(); }
        catch (Exception ex) { _captureState.Text = "Shadow Capture: " + ex.Message; }
    }

    private async Task StopCaptureAsync()
    {
        try { _captureState.Text = await _shadow.StopAsync(); }
        catch (Exception ex) { _captureState.Text = "Shadow Capture: " + ex.Message; }
    }

    private async Task RefreshCaptureStatusAsync()
    {
        var status = await _shadow.GetStatusAsync();
        var settings = _shadow.LoadSettings();
        _captureState.Text = $"{status.Detail}\nمدة {settings.ReplaySeconds}s • Hotkey {settings.SaveHotkey} • Folder {settings.SaveFolder}";
    }

    private void ToggleHud()
    {
        if (_hud != null)
        {
            try { _hud.Close(); } catch { }
            _hud = null;
            SetStatus("تم إيقاف D7 HUD.");
            return;
        }
        var game = CurrentGame();
        if (string.IsNullOrWhiteSpace(game))
        {
            MessageBox.Show("افتح اللعبة أولًا حتى يربط D7 HUD القياسات بعملية اللعبة.", "D7KT • HUD");
            return;
        }
        _hud = new GameOverlayWindow(_hardware, game) { Owner = this };
        _hud.Closed += (_, _) => _hud = null;
        _hud.Show();
        SetStatus("D7 HUD يعمل.");
    }

    private async Task ScanNetworkAsync() => await ScanNetworkAsync(true);

    private async Task ScanNetworkAsync(bool updateStatus)
    {
        try
        {
            if (updateStatus) _networkState.Text = "جاري القياس…";
            var report = await _network.ScanAsync();
            _networkState.Text = $"{report.AdapterName} • {report.IPv4}\nPing {Ms(report.InternetLatencyMs)} • Jitter {Ms(report.JitterMs)} • Loss {report.PacketLossPercent:0.#}% • Link {(report.LinkSpeedBps > 0 ? report.LinkSpeedBps / 1_000_000d + " Mbps" : "—")}\n{report.Notes}";
        }
        catch (Exception ex) { _networkState.Text = "Network: " + ex.Message; }
    }

    private async Task ApplyGamingNetworkAsync()
    {
        if (MessageBox.Show("Gaming Network يحفظ Backup ثم يعدّل فقط خصائص NIC الآمنة المعروفة. قد ينقطع الاتصال عدة ثوانٍ. متابعة؟", "D7KT • Network", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _networkState.Text = "جاري تطبيق Gaming Network…";
        var result = await _networkProfile.ApplyAsync();
        _networkState.Text = result.Detail;
        await Task.Delay(5000);
        await ScanNetworkAsync();
    }

    private async Task RestoreGamingNetworkAsync()
    {
        _networkState.Text = "جاري استعادة إعدادات NIC…";
        var result = await _networkProfile.RestoreAsync();
        _networkState.Text = result.Detail;
        await Task.Delay(4000);
        await ScanNetworkAsync();
    }

    private async Task CheckAndInstallUpdateAsync(bool install, bool silent = false)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        try
        {
            _updateProgress.Value = 0;
            _updateState.Text = "جاري التحقق من آخر إصدار…";
            var info = await _updates.CheckAsync();
            if (!info.UpdateAvailable)
            {
                _updateState.Text = $"أنت على أحدث إصدار • {_updates.CurrentVersionText}";
                return;
            }

            _updateState.Text = $"متوفر v{info.LatestVersion.ToString(3)} • إصدارك {_updates.CurrentVersionText}";
            if (!install)
            {
                if (!silent) MessageBox.Show(_updateState.Text, "D7KT • Update");
                return;
            }

            if (MessageBox.Show($"تحديث D7KT إلى v{info.LatestVersion.ToString(3)}؟\n\nسيتم التنزيل والتحقق من SHA-256 ثم تشغيل المثبت من داخل D7KT.", "D7KT • Update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            var progress = new Progress<double>(p =>
            {
                _updateProgress.Value = p;
                _updateState.Text = $"تنزيل v{info.LatestVersion.ToString(3)}… {p:0}%";
            });
            var installer = await _updates.DownloadAndVerifyAsync(info, progress);
            _updateState.Text = "SHA-256 صحيح • جاري تشغيل المثبت…";
            _updateProgress.Value = 100;
            D7UpdateService.LaunchInstaller(installer);
            await Task.Delay(1200);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _updateState.Text = "فشل التحديث: " + ex.Message;
            if (!silent) MessageBox.Show(_updateState.Text, "D7KT • Update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _updateBusy = false; }
    }

    private void RegisterCaptureHotkey()
    {
        UnregisterCaptureHotkey();
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (_windowHandle == IntPtr.Zero) return;
        _hotkeySource = HwndSource.FromHwnd(_windowHandle);
        _hotkeySource?.AddHook(HotkeyWndProc);
        var key = ParseFunctionKey(_shadow.LoadSettings().SaveHotkey);
        if (key == 0) key = 0x77;
        if (!RegisterHotKey(_windowHandle, ShadowCaptureHotkeyId, 0, (uint)key))
            SetStatus("Shadow Capture يعمل لكن Hotkey مستخدم من برنامج آخر. غيّره من إعدادات الالتقاط.");
    }

    private void UnregisterCaptureHotkey()
    {
        if (_windowHandle != IntPtr.Zero) UnregisterHotKey(_windowHandle, ShadowCaptureHotkeyId);
        if (_hotkeySource != null) _hotkeySource.RemoveHook(HotkeyWndProc);
        _hotkeySource = null;
        _windowHandle = IntPtr.Zero;
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == ShadowCaptureHotkeyId)
        {
            handled = true;
            _ = SaveReplayAsync();
        }
        return IntPtr.Zero;
    }

    private static int ParseFunctionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var text = value.Trim().ToUpperInvariant();
        if (!text.StartsWith('F') || !int.TryParse(text[1..], out var number) || number is < 1 or > 24) return 0;
        return 0x70 + number - 1;
    }

    private async Task DisposeAsync()
    {
        _timer.Stop();
        UnregisterCaptureHotkey();
        try { _hud?.Close(); } catch { }
        try { _contract.Dispose(); } catch { }
        try { _smartFans.Dispose(); } catch { }
        try { await _sessions.DisposeAsync(); } catch { }
        try { await _missions.DisposeAsync(); } catch { }
        try { await _shadow.DisposeAsync(); } catch { }
        try { _hardware.Dispose(); } catch { }
    }

    private void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        _pageHost.Content = page;
        foreach (var pair in _navButtons)
        {
            var selected = pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected ? Brush("AccentSoft") : Brushes.Transparent;
            pair.Value.BorderBrush = selected ? Brush("Accent") : Brushes.Transparent;
        }
        if (key == "capture") _ = RefreshCaptureStatusAsync();
        if (key == "updates") _ = CheckAndInstallUpdateAsync(false, silent: true);
    }

    private Border MetricCard(string title, TextBlock value, TextBlock sub)
    {
        var card = Card();
        card.Margin = new Thickness(4);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brush("Muted"), FontSize = 11, FlowDirection = FlowDirection.LeftToRight });
        value.FontSize = 29;
        value.FontWeight = FontWeights.Black;
        value.Foreground = Brush("Text");
        value.FlowDirection = FlowDirection.LeftToRight;
        value.Margin = new Thickness(0, 6, 0, 3);
        stack.Children.Add(value);
        sub.Foreground = Brush("Muted");
        sub.FontSize = 10.5;
        sub.TextWrapping = TextWrapping.Wrap;
        sub.FlowDirection = FlowDirection.LeftToRight;
        stack.Children.Add(sub);
        card.Child = stack;
        return card;
    }

    private Border MissionCard(string title, string description, D7Mission mission)
        => RepairHero(title, description, "تشغيل", async () => await ApplyMissionAsync(mission));

    private Border RepairHero(string title, string description, string action, Func<Task> run)
    {
        var card = Card();
        card.Margin = new Thickness(4);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = description, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12), MinHeight = 38 });
        var button = ActionButton(action, run, true);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(button);
        card.Child = stack;
        return card;
    }

    private Border ToolCard(string title, string description, Action open)
    {
        var card = Card();
        card.Margin = new Thickness(4);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = description, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12), MinHeight = 38 });
        var button = new Button { Content = "فتح", HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => open();
        stack.Children.Add(button);
        card.Child = stack;
        return card;
    }

    private Border QuickCard(string title, string description, Action open)
    {
        var card = Card();
        card.Margin = new Thickness(4);
        card.Cursor = Cursors.Hand;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = description, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        card.Child = stack;
        card.MouseLeftButtonUp += (_, _) => open();
        return card;
    }

    private Border Card() => new()
    {
        Background = Brush("Panel"),
        BorderBrush = Brush("Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(16),
        Margin = new Thickness(4)
    };

    private UIElement HeaderBlock(string title, string subtitle)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("Muted"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        return stack;
    }

    private UIElement SectionTitle(string title, string subtitle)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return stack;
    }

    private Border EmptyState(string text) => new()
    {
        Background = Brush("Panel"),
        BorderBrush = Brush("Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(22),
        Child = new TextBlock { Text = text, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center }
    };

    private Button ActionButton(string text, Func<Task> run, bool accent = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 136,
            Margin = new Thickness(4),
            Background = accent ? Brush("AccentStrong") : Brush("Panel2"),
            BorderBrush = accent ? Brush("Accent") : Brush("Border")
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await run(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "D7KT", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private void OpenDialog(Window window)
    {
        window.Owner = this;
        window.Icon ??= Icon;
        window.ShowDialog();
    }

    private string? CurrentGame() => _orchestrator.LastStatus?.Context.PrimaryGame;

    private void SetStatus(string text) => _status.Text = text.Replace(Environment.NewLine, " • ");

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private Brush SeverityBrush(string value)
    {
        if (value.Contains("حرج", StringComparison.OrdinalIgnoreCase) || value.Contains("critical", StringComparison.OrdinalIgnoreCase)) return Brush("Danger");
        if (value.Contains("تحذير", StringComparison.OrdinalIgnoreCase) || value.Contains("warning", StringComparison.OrdinalIgnoreCase)) return Brush("Warning");
        if (value.Contains("سليم", StringComparison.OrdinalIgnoreCase) || value.Contains("ok", StringComparison.OrdinalIgnoreCase)) return Brush("Success");
        return Brush("Accent");
    }

    private Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    private static string Ms(double? value) => value.HasValue ? $"{value.Value:0.0}ms" : "—";

    private static void ShowLongResult(string title, string result)
    {
        var window = new Window
        {
            Title = title,
            Width = 900,
            Height = 620,
            MinWidth = 700,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)Application.Current.FindResource("Bg"),
            Foreground = (Brush)Application.Current.FindResource("Text")
        };
        window.Content = new TextBox
        {
            Text = result,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Margin = new Thickness(14)
        };
        if (Application.Current.MainWindow != null) window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
