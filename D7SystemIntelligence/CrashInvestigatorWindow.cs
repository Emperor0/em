using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class CrashInvestigatorWindow : Window
{
    private readonly CrashInvestigatorService _service = new();
    private readonly ComboBox _range = new();
    private readonly TextBlock _verdict = new();
    private readonly DataGrid _grid = new();
    private readonly TextBox _detail = new();

    public CrashInvestigatorWindow()
    {
        Title = "D7 NEXUS • Crash Investigator";
        Width = 1120;
        Height = 760;
        MinWidth = 920;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
        Loaded += async (_, _) => await ScanAsync();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "CRASH INVESTIGATOR", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Windows Event Logs مصفاة للأعطال المهمة: Application crash، WHEA، GPU/Display، Storage وKernel-Power. Event correlation بدون اختلاق سبب غير مثبت.", Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 12) };
        _range.Items.Add(new ComboBoxItem { Content = "آخر 6 ساعات", Tag = 6d });
        _range.Items.Add(new ComboBoxItem { Content = "آخر 24 ساعة", Tag = 24d });
        _range.Items.Add(new ComboBoxItem { Content = "آخر 3 أيام", Tag = 72d });
        _range.Items.Add(new ComboBoxItem { Content = "آخر 7 أيام", Tag = 168d });
        _range.SelectedIndex = 1; _range.MinWidth = 160;
        var scan = new Button { Content = "فحص الآن", MinWidth = 120 };
        scan.Click += async (_, _) => await ScanAsync();
        controls.Children.Add(_range); controls.Children.Add(scan);
        Grid.SetRow(controls, 1); root.Children.Add(controls);

        var verdictCard = new Border { Background = (Brush)Application.Current.FindResource("AccentSoft"), BorderBrush = (Brush)Application.Current.FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), Child = _verdict };
        _verdict.TextWrapping = TextWrapping.Wrap; _verdict.FontSize = 14;
        Grid.SetRow(verdictCard, 2); root.Children.Add(verdictCard);

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true; _grid.SelectionChanged += (_, _) => ShowDetail();
        _grid.Columns.Add(new DataGridTextColumn { Header = "الوقت", Binding = new System.Windows.Data.Binding(nameof(StabilityEventRecord.TimeCreated)) { StringFormat = "yyyy-MM-dd HH:mm:ss" }, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الفئة", Binding = new System.Windows.Data.Binding(nameof(StabilityEventRecord.Category)), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Provider", Binding = new System.Windows.Data.Binding(nameof(StabilityEventRecord.Provider)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new System.Windows.Data.Binding(nameof(StabilityEventRecord.EventId)), Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الملخص", Binding = new System.Windows.Data.Binding(nameof(StabilityEventRecord.Summary)), Width = new DataGridLength(3.2, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 3); root.Children.Add(_grid);

        _detail.IsReadOnly = true; _detail.AcceptsReturn = true; _detail.TextWrapping = TextWrapping.Wrap; _detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; _detail.Margin = new Thickness(0, 12, 0, 0); _detail.Background = (Brush)Application.Current.FindResource("Panel");
        Grid.SetRow(_detail, 4); root.Children.Add(_detail);
        return root;
    }

    private async Task ScanAsync()
    {
        _verdict.Text = "جاري قراءة Windows Event Logs…";
        try
        {
            var hours = _range.SelectedItem is ComboBoxItem item && item.Tag is double h ? h : 24d;
            var report = await _service.ScanAsync(TimeSpan.FromHours(hours));
            _grid.ItemsSource = report.Events;
            _verdict.Text = $"{report.Verdict}\nApp crashes {report.AppCrashes} • WHEA {report.HardwareErrors} • GPU {report.GpuDriverEvents} • Storage {report.StorageEvents} • Unexpected shutdown {report.UnexpectedShutdowns}";
            if (_grid.Items.Count > 0) _grid.SelectedIndex = 0; else _detail.Text = "لا توجد أحداث مهمة في الفترة.";
        }
        catch (Exception ex) { _verdict.Text = "Crash Investigator: " + ex.Message; }
    }

    private void ShowDetail()
    {
        if (_grid.SelectedItem is not StabilityEventRecord e) return;
        _detail.Text = $"{e.TimeCreated:O}\n{e.Category} • {e.Provider} • Event {e.EventId} • {e.Level}\n\n{e.Message}";
    }
}
