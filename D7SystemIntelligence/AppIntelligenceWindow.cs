using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class AppIntelligenceWindow : Window
{
    private readonly AppIntelligenceService _service = new();
    private readonly StackPanel _apps = new();
    private readonly TextBlock _status = new();
    private readonly Button _refresh = new();
    private bool _busy;

    public AppIntelligenceWindow()
    {
        Title = "D7KT • App Intelligence";
        Width = 1180;
        Height = 790;
        MinWidth = 980;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("Bg");
        Foreground = B("Text");
        FlowDirection = FlowDirection.RightToLeft;
        Content = BuildUi();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "APP INTELLIGENCE", FontSize = 30, FontWeight = FontWeights.Black, Foreground = B("Accent") });
        title.Children.Add(new TextBlock
        {
            Text = "D7KT يدير السلوك القابل للقياس والرجوع فقط. لا يعدل إعداد proprietary مجهول، ولا يقتل خدمة تعريف/صوت/Anti-Cheat لمجرد توفير موارد.",
            Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        _refresh.Content = "إعادة الفحص";
        _refresh.MinWidth = 125;
        _refresh.VerticalAlignment = VerticalAlignment.Center;
        _refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(_refresh, 1);
        header.Children.Add(_refresh);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var statusCard = new Border
        {
            Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(13), Margin = new Thickness(0, 14, 0, 12), Child = _status
        };
        _status.Text = "جاري اكتشاف البرامج…";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = B("Muted");
        Grid.SetRow(statusCard, 1);
        root.Children.Add(statusCard);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.Content = _apps;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        return root;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        _refresh.IsEnabled = false;
        _status.Text = "جاري اكتشاف التطبيقات والعمليات والقدرات الآمنة…";
        try
        {
            var apps = await _service.ScanAsync();
            _apps.Children.Clear();
            foreach (var app in apps.OrderByDescending(x => x.Running).ThenByDescending(x => x.Installed).ThenBy(x => x.DisplayName))
                _apps.Children.Add(AppCard(app));
            var installed = apps.Count(x => x.Installed);
            var running = apps.Count(x => x.Running);
            _status.Text = $"تم اكتشاف {installed}/{apps.Count} برنامج • يعمل الآن {running}. كل زر أدناه له مسار Restore أو Safety guard واضح.";
        }
        catch (Exception ex) { _status.Text = "App Intelligence: " + ex.Message; }
        finally { _busy = false; _refresh.IsEnabled = true; }
    }

    private Border AppCard(ManagedAppState app)
    {
        var card = new Border
        {
            Background = B("Panel"), BorderBrush = app.Running ? B("Accent") : B("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10)
        };
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        var info = new StackPanel();
        var top = new WrapPanel();
        top.Children.Add(new TextBlock { Text = app.DisplayName, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 12, 0) });
        top.Children.Add(Badge(app.Installed ? "INSTALLED" : "NOT FOUND", app.Installed ? B("Success") : B("Muted")));
        top.Children.Add(Badge(app.Running ? "RUNNING" : "STOPPED", app.Running ? B("Accent") : B("Muted")));
        info.Children.Add(top);
        if (!string.IsNullOrWhiteSpace(app.ExecutablePath))
            info.Children.Add(new TextBlock { Text = app.ExecutablePath, FlowDirection = FlowDirection.LeftToRight, Foreground = B("Muted"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 3) });
        if (app.RunningProcesses.Count > 0)
            info.Children.Add(new TextBlock { Text = "Processes: " + string.Join(", ", app.RunningProcesses), FlowDirection = FlowDirection.LeftToRight, Foreground = B("Muted"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap });

        var capabilityGrid = new WrapPanel { Margin = new Thickness(0, 10, 0, 7) };
        foreach (var capability in app.Capabilities)
            capabilityGrid.Children.Add(Badge((capability.Supported ? "✓ " : "○ ") + capability.Name, capability.Supported ? B("Success") : B("Muted")));
        info.Children.Add(capabilityGrid);
        info.Children.Add(new TextBlock { Text = app.SafetyNote, Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 11.2 });
        Grid.SetColumn(info, 0);
        root.Children.Add(info);

        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var profiles = new UniformGrid { Columns = 3 };
        profiles.Children.Add(Action("Normal", async () => await RunAsync(() => _service.ApplyProfileAsync(app.Id, AppProfileMode.Normal))));
        profiles.Children.Add(Action("Gaming", async () => await RunAsync(() => _service.ApplyProfileAsync(app.Id, AppProfileMode.Gaming)), true));
        profiles.Children.Add(Action("Streaming", async () => await RunAsync(() => _service.ApplyProfileAsync(app.Id, AppProfileMode.Streaming))));
        actions.Children.Add(profiles);

        var line2 = new UniformGrid { Columns = 3 };
        line2.Children.Add(Action("Restore", async () => await RunAsync(() => _service.RestoreProfileAsync(app.Id))));
        line2.Children.Add(Action("Startup OFF", async () => await RunAsync(() => _service.DisableStartupAsync(app.Id))));
        line2.Children.Add(Action("Startup Restore", async () => await RunAsync(() => _service.RestoreStartupAsync(app.Id))));
        actions.Children.Add(line2);

        var line3 = new UniformGrid { Columns = 3 };
        line3.Children.Add(Action("Safe Cache", async () => await RunAsync(() => _service.CleanSafeCacheAsync(app.Id))));
        line3.Children.Add(Action("فتح البرنامج", async () => { _status.Text = _service.OpenApp(app.Id); await Task.CompletedTask; }));
        line3.Children.Add(Action("إغلاق UI", async () =>
        {
            if (MessageBox.Show($"إغلاق واجهة {app.DisplayName} فقط حسب قائمة D7KT الآمنة؟", "D7KT • App Intelligence", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await RunAsync(() => _service.StopUserInterfaceAsync(app.Id));
        }));
        actions.Children.Add(line3);
        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);

        card.Child = root;
        return card;
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _status.Text = "جاري التنفيذ والتحقق…";
            _status.Text = await action();
        }
        catch (Exception ex) { _status.Text = "فشل الإجراء: " + ex.Message; }
        finally { _busy = false; }
    }

    private Button Action(string text, Func<Task> action, bool accent = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), MinWidth = 96, Background = accent ? B("AccentStrong") : B("Panel2"), BorderBrush = accent ? B("Accent") : B("Border") };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Border Badge(string text, Brush color)
        => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), BorderBrush = color, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(3, 1, 3, 1),
            Child = new TextBlock { Text = text, Foreground = color, FontSize = 9.5, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight }
        };

    private Brush B(string key) => (Brush)Application.Current.FindResource(key);
}
