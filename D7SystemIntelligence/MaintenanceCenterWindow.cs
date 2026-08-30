using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class MaintenanceCenterWindow : Window
{
    private readonly SafeMaintenanceService _maintenance = new();
    private readonly TextBlock _guard = new();
    private readonly TextBox _output = new();
    private readonly ProgressBar _progress = new() { Height = 7, IsIndeterminate = false };
    private readonly List<Button> _buttons = [];

    public MaintenanceCenterWindow()
    {
        Title = "D7KT • Maintenance Center";
        Width = 1000;
        Height = 720;
        MinWidth = 840;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("Bg");
        Foreground = B("Text");
        FlowDirection = FlowDirection.RightToLeft;
        Content = Build();
        Loaded += async (_, _) => await ScanAsync();
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "MAINTENANCE CENTER", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = B("Accent") });
        header.Children.Add(new TextBlock
        {
            Text = "Scan → Plan → Apply. لا Registry tweak packs، لا BIOS/Firmware، ولا Updates ثقيلة أثناء اللعب/البث. Startup/Background/Removal موجودة هنا لأنها صيانة، وليست Features رئيسية.",
            Foreground = B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var guardCard = Card();
        _guard.Text = "جاري فحص حالة الجلسة…";
        _guard.TextWrapping = TextWrapping.Wrap;
        _guard.FontSize = 14;
        guardCard.Child = _guard;
        Grid.SetRow(guardCard, 1); root.Children.Add(guardCard);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        actions.Children.Add(Btn("Scan + Plan", ScanAsync, true));
        actions.Children.Add(Btn("Apply Safe Updates", ApplyAsync, true));
        actions.Children.Add(Btn("Startup Manager", () => OpenAsync(new StartupManagerWindow())));
        actions.Children.Add(Btn("Background Apps", () => OpenAsync(new BackgroundAppsWindow())));
        actions.Children.Add(Btn("Smart Removal", () => OpenAsync(new SmartRemovalWindow())));
        actions.Children.Add(Btn("Restore Vault", () => OpenAsync(new RestoreVaultWindow())));
        Grid.SetRow(actions, 2); root.Children.Add(actions);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _progress.Margin = new Thickness(4, 0, 4, 8);
        Grid.SetRow(_progress, 0); content.Children.Add(_progress);
        _output.IsReadOnly = true;
        _output.AcceptsReturn = true;
        _output.TextWrapping = TextWrapping.Wrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Grid.SetRow(_output, 1); content.Children.Add(_output);
        Grid.SetRow(content, 3); root.Children.Add(content);
        return root;
    }

    private async Task ScanAsync()
    {
        await Busy(async () =>
        {
            var plan = await _maintenance.BuildPlanAsync();
            _guard.Text = plan.GuardReason;
            _guard.Foreground = plan.AllowedNow ? B("Success") : B("Warning");
            var lines = new List<string>
            {
                plan.Summary,
                "",
                "=== APPS / WINGET ===",
                plan.AppScan,
                "",
                $"=== DRIVER UPDATES ({plan.DriverUpdates.Count}) ==="
            };
            lines.AddRange(plan.DriverUpdates.Select(x => "• " + x.Title));
            _output.Text = string.Join(Environment.NewLine, lines);
        });
    }

    private async Task ApplyAsync()
    {
        var plan = await _maintenance.BuildPlanAsync();
        _guard.Text = plan.GuardReason;
        if (!plan.AllowedNow)
        {
            _guard.Foreground = B("Warning");
            _output.Text = "BLOCKED\r\n" + plan.GuardReason;
            return;
        }

        if (MessageBox.Show(
                "تطبيق الخطة الآمنة الآن؟\n\nApps: winget upgrades\nDrivers: فقط Windows Update driver path بعد Driver Store backup وRestore Point attempt.\nBIOS/Firmware: لن يتم لمسها.",
                "D7KT • Maintenance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        await Busy(async () =>
        {
            var progress = new Progress<string>(x => _guard.Text = x);
            var result = await _maintenance.RunUpdatesAsync(progress);
            _output.Text = result.Detail;
            _guard.Text = result.RebootRequired
                ? "اكتمل التنفيذ لكن Windows يطلب Restart لبعض النتائج؛ Post-update driver verification تبقى Pending حتى إعادة التشغيل."
                : result.Success ? "اكتمل Maintenance Apply." : "اكتملت العملية مع عناصر غير مثبتة/فاشلة. راجع التفاصيل.";
            _guard.Foreground = result.Success ? B("Success") : B("Warning");
        });
    }

    private Task OpenAsync(Window window)
    {
        window.Owner = this;
        window.Icon = Icon;
        window.ShowDialog();
        return Task.CompletedTask;
    }

    private async Task Busy(Func<Task> action)
    {
        SetBusy(true);
        try { await action(); }
        catch (Exception ex)
        {
            _guard.Text = "Maintenance: " + ex.Message;
            _guard.Foreground = B("Danger");
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        foreach (var button in _buttons) button.IsEnabled = !busy;
        _progress.IsIndeterminate = busy;
    }

    private Button Btn(string text, Func<Task> action, bool accent = false)
    {
        var b = new Button
        {
            Content = text,
            MinWidth = 135,
            Background = accent ? B("AccentStrong") : B("Panel2"),
            BorderBrush = accent ? B("Accent") : B("Border")
        };
        b.Click += async (_, _) => await action();
        _buttons.Add(b);
        return b;
    }

    private Border Card() => new()
    {
        Background = B("Panel"), BorderBrush = B("Border"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(15), Padding = new Thickness(15), Margin = new Thickness(4, 14, 4, 0)
    };

    private Brush B(string key) => (Brush)Application.Current.FindResource(key);
}
