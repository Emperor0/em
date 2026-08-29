using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class SessionHistoryWindow : Window
{
    private readonly GameSessionService _service;
    private readonly DataGrid _grid = new();
    private readonly TextBox _detail = new();

    public SessionHistoryWindow(GameSessionService service)
    {
        _service = service;
        Title = "D7 NEXUS • Game Sessions";
        Width = 1050;
        Height = 720;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
        Reload();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 16) };
        var refresh = new Button { Content = "تحديث", MinWidth = 100 };
        refresh.Click += (_, _) => Reload();
        DockPanel.SetDock(refresh, Dock.Left);
        header.Children.Add(refresh);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "GAME SESSIONS", FontSize = 28, FontWeight = FontWeights.Bold });
        titles.Children.Add(new TextBlock { Text = "FPS / 1% Low / P99 / الحرارة / الضغط / Stutter Black Box محفوظة محليًا.", Foreground = (Brush)Application.Current.FindResource("Muted") });
        header.Children.Add(titles);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionChanged += (_, _) => ShowSelected();
        _grid.Columns.Add(new DataGridTextColumn { Header = "اللعبة", Binding = new System.Windows.Data.Binding("Game"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "البداية", Binding = new System.Windows.Data.Binding("StartedAt") { StringFormat = "yyyy-MM-dd HH:mm" }, Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "دقيقة", Binding = new System.Windows.Data.Binding("DurationMinutes") { StringFormat = "0.0" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Avg FPS", Binding = new System.Windows.Data.Binding("AverageFps") { StringFormat = "0" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "1% Low", Binding = new System.Windows.Data.Binding("AverageOnePercentLow") { StringFormat = "0" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "P99 worst", Binding = new System.Windows.Data.Binding("WorstP99FrameMs") { StringFormat = "0.0 ms" }, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Stutters", Binding = new System.Windows.Data.Binding("StutterCount"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        _detail.IsReadOnly = true;
        _detail.AcceptsReturn = true;
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detail.Background = (Brush)Application.Current.FindResource("Panel");
        _detail.BorderBrush = (Brush)Application.Current.FindResource("Border");
        _detail.Margin = new Thickness(0, 14, 0, 0);
        _detail.FontFamily = new FontFamily("Consolas");
        Grid.SetRow(_detail, 2);
        root.Children.Add(_detail);
        return root;
    }

    private void Reload()
    {
        _grid.ItemsSource = _service.ListRecent(100);
        if (_grid.Items.Count > 0) _grid.SelectedIndex = 0;
        else _detail.Text = _service.IsRunning ? $"جلسة {_service.ActiveGame} شغالة الآن؛ التقرير يثبت عند إغلاق اللعبة." : "لا توجد جلسات محفوظة بعد.";
    }

    private void ShowSelected()
    {
        if (_grid.SelectedItem is not GameSessionReport r) return;
        var lines = new List<string>
        {
            r.Summary,
            $"CPU max {r.MaxCpuLoad:0}% • {r.MaxCpuTemp:0}°C",
            $"GPU max {r.MaxGpuLoad:0}% • {r.MaxGpuTemp:0}°C",
            $"RAM max {r.MaxRamLoad:0}% • Ping avg {(r.AveragePingMs.HasValue ? r.AveragePingMs.Value.ToString("0.0") + "ms" : "—")}",
            $"Stutters: {r.StutterCount}",
            ""
        };
        foreach (var s in r.Stutters.TakeLast(30))
            lines.Add($"{s.At:HH:mm:ss} • worst {s.WorstFrameMs:0.0}ms • P99 {s.P99FrameMs:0.0}ms • CPU {s.CpuLoad:0}% • GPU {s.GpuLoad:0}% • {s.LikelyCause}");
        lines.Add("");
        lines.Add("ملف التقرير: " + r.FilePath);
        _detail.Text = string.Join(Environment.NewLine, lines);
    }
}
