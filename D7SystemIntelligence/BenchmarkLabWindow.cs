using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class BenchmarkLabWindow : Window
{
    private readonly BenchmarkLabService _service;
    private readonly TextBox _label = new() { Text = "Baseline" };
    private readonly ComboBox _duration = new();
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _comparison = new();

    public BenchmarkLabWindow(BenchmarkLabService service)
    {
        _service = service;
        Title = "D7KT • Benchmark Lab";
        Width = 1200;
        Height = 800;
        MinWidth = 980;
        MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
        Loaded += (_, _) => Reload();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "BENCHMARK LAB", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "A/B بدون Placebo: يستخدم raw frametimes من PresentMon الموجود أصلًا داخل Game Session. يحسب FPS و1% و0.1% وP95/P99/P99.9 ثم يعطي KEEP/REJECT مع Confidence.",
            Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var controls = new WrapPanel { Margin = new Thickness(0, 16, 0, 10) };
        controls.Children.Add(new TextBlock { Text = "الاسم", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) });
        _label.Width = 190; controls.Children.Add(_label);
        controls.Children.Add(new TextBlock { Text = "المدة", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 5, 5, 5) });
        foreach (var seconds in new[] { 30, 60, 120 }) _duration.Items.Add(new ComboBoxItem { Content = $"{seconds} ثانية", Tag = seconds });
        _duration.SelectedIndex = 1; _duration.Width = 140; controls.Children.Add(_duration);
        var run = new Button { Content = "ابدأ القياس", MinWidth = 120, Background = (Brush)Application.Current.FindResource("AccentStrong") };
        run.Click += async (_, _) => await RunAsync(run);
        var refresh = new Button { Content = "تحديث", MinWidth = 95 };
        refresh.Click += (_, _) => Reload();
        var compare = new Button { Content = "A/B للمحددين", MinWidth = 130 };
        compare.Click += (_, _) => CompareSelected();
        controls.Children.Add(run); controls.Children.Add(refresh); controls.Children.Add(compare);
        Grid.SetRow(controls, 1); root.Children.Add(controls);

        var statusCard = new Border
        {
            Background = (Brush)Application.Current.FindResource("AccentSoft"),
            BorderBrush = (Brush)Application.Current.FindResource("Border"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 0, 10), Child = _status
        };
        _status.Text = "لـA/B صالح: استخدم نفس اللعبة، نفس المشهد، ومدد متقاربة. D7KT يخفض Confidence إذا الاختبار غير متقارب.";
        _status.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(statusCard, 2); root.Children.Add(statusCard);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.Columns.Add(new DataGridTextColumn { Header = "الاسم", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.Label)), Width = new DataGridLength(1.25, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "اللعبة", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.Game)), Width = new DataGridLength(1.25, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "FPS", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.AverageFps)) { StringFormat = "0.0" }, Width = new DataGridLength(.65, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "1%", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.AverageOnePercentLow)) { StringFormat = "0.0" }, Width = new DataGridLength(.65, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "0.1%", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.PointOnePercentLow)) { StringFormat = "0.0" }, Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "P99", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.AverageP99FrameMs)) { StringFormat = "0.00 ms" }, Width = new DataGridLength(.75, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Frames", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.FrameCount)) { StringFormat = "N0" }, Width = new DataGridLength(.75, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "CPU%", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.AverageCpuLoad)) { StringFormat = "0.0" }, Width = new DataGridLength(.65, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "GPU%", Binding = new System.Windows.Data.Binding(nameof(BenchmarkSnapshot.AverageGpuLoad)) { StringFormat = "0.0" }, Width = new DataGridLength(.65, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 3); root.Children.Add(_grid);

        _comparison.IsReadOnly = true;
        _comparison.AcceptsReturn = true;
        _comparison.TextWrapping = TextWrapping.Wrap;
        _comparison.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _comparison.Background = (Brush)Application.Current.FindResource("Panel");
        _comparison.Margin = new Thickness(0, 12, 0, 0);
        _comparison.FontFamily = new FontFamily("Consolas");
        Grid.SetRow(_comparison, 4); root.Children.Add(_comparison);
        return root;
    }

    private async Task RunAsync(Button run)
    {
        run.IsEnabled = false;
        try
        {
            var seconds = _duration.SelectedItem is ComboBoxItem item && item.Tag is int s ? s : 60;
            var progress = new Progress<string>(x => _status.Text = x);
            var result = await _service.CaptureAsync(_label.Text, TimeSpan.FromSeconds(seconds), progress);
            _status.Text =
                $"تم القياس • {result.Label} • Raw frames {result.FrameCount:N0} • FPS {F(result.AverageFps)} • 1% {F(result.AverageOnePercentLow)} • 0.1% {F(result.PointOnePercentLow)} • P99 {F(result.AverageP99FrameMs)}ms • {result.Quality}";
            Reload();
        }
        catch (Exception ex) { _status.Text = "Benchmark Lab: " + ex.Message; }
        finally { run.IsEnabled = true; }
    }

    private void Reload() => _grid.ItemsSource = _service.List(100);

    private void CompareSelected()
    {
        var selected = _grid.SelectedItems.Cast<BenchmarkSnapshot>().OrderBy(x => x.StartedAt).ToArray();
        if (selected.Length != 2)
        {
            _comparison.Text = "حدد قياسين بالضبط: Baseline ثم Candidate.";
            return;
        }

        var c = _service.Compare(selected[0], selected[1]);
        static string P(double? v) => v.HasValue ? v.Value.ToString("+0.0;-0.0;0.0") + "%" : "—";
        _comparison.Text =
            $"Baseline: {c.Baseline.Label} • {c.Baseline.FrameCount:N0} frames • {c.Baseline.DurationSeconds:0}s • {c.Baseline.Quality}\r\n" +
            $"Candidate: {c.Candidate.Label} • {c.Candidate.FrameCount:N0} frames • {c.Candidate.DurationSeconds:0}s • {c.Candidate.Quality}\r\n\r\n" +
            $"FPS Δ       {P(c.FpsDeltaPercent)}\r\n" +
            $"1% Low Δ    {P(c.OnePercentLowDeltaPercent)}\r\n" +
            $"0.1% Low Δ  {P(c.PointOneLowDeltaPercent)}\r\n" +
            $"P99 Δ       {P(c.P99DeltaPercent)} (positive = better)\r\n" +
            $"Weighted    {(c.WeightedScore.HasValue ? c.WeightedScore.Value.ToString("+0.00;-0.00;0.00") + "%" : "—")}\r\n" +
            $"Confidence  {c.Confidence}\r\n\r\n" +
            $"CPU load Δ  {c.CpuLoadDelta:+0.0;-0.0;0.0}%\r\nGPU load Δ  {c.GpuLoadDelta:+0.0;-0.0;0.0}%\r\n" +
            $"CPU temp Δ  {c.CpuTempDelta:+0.0;-0.0;0.0}°C\r\nGPU temp Δ  {c.GpuTempDelta:+0.0;-0.0;0.0}°C\r\n\r\n{c.Verdict}";
    }

    private static string F(double? value) => value.HasValue ? value.Value.ToString("0.0") : "—";
}
