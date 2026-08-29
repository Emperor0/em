using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class BackgroundAppsWindow : Window
{
    private readonly BackgroundAppManagerService _service = new();
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();
    private readonly Button _smartClean = new() { Content = "تنظيف ذكي الآن" };
    private readonly Button _refresh = new() { Content = "إعادة الفحص" };

    public BackgroundAppsWindow()
    {
        Title = "D7 — تطبيقات الخلفية";
        Width = 1240;
        Height = 760;
        MinWidth = 980;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "تطبيقات الخلفية — Smart Clean", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "D7 يقيس CPU/RAM ويصنف العمليات. Smart Clean يغلق فقط العناصر التي حصلت على ثقة Safe-To-Close ولا يلمس Windows أو التعريفات أو الصوت أو Anti-Cheat أو أي برنامج له نافذة مستخدمة.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Muted", Brushes.Gray),
            Margin = new Thickness(0, 6, 0, 10)
        });
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _refresh.Click += async (_, _) => await RefreshAsync();
        _smartClean.Click += async (_, _) => await SmartCleanAsync();
        headerButtons.Children.Add(_smartClean);
        headerButtons.Children.Add(_refresh);
        header.Children.Add(headerButtons);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.Margin = new Thickness(0, 14, 0, 14);
        _grid.Columns.Add(new DataGridTextColumn { Header = "القرار", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.DecisionText)), Width = new DataGridLength(.8, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "التطبيق", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.Name)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "RAM MB", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.MemoryMb)) { StringFormat = "0" }, Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "CPU %", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.CpuPercent)) { StringFormat = "0.0" }, Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "نافذة", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.HasVisibleWindow)), Width = new DataGridLength(.45, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الناشر", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.Publisher)), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "السبب", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.Reason)), Width = new DataGridLength(2.1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "المسار", Binding = new System.Windows.Data.Binding(nameof(BackgroundProcessRecord.ExecutablePath)), Width = new DataGridLength(2.6, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var footer = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var close = new Button { Content = "إغلاق المحدد" };
        close.Click += async (_, _) => await CloseSelectedAsync();
        var alwaysClose = new Button { Content = "دائمًا قابل للإغلاق" };
        alwaysClose.Click += (_, _) => SetPolicy(true);
        var alwaysKeep = new Button { Content = "احمه دائمًا" };
        alwaysKeep.Click += (_, _) => SetPolicy(false);
        var reset = new Button { Content = "إلغاء القرار المحفوظ" };
        reset.Click += (_, _) => ResetPolicy();
        row.Children.Add(close);
        row.Children.Add(alwaysClose);
        row.Children.Add(alwaysKeep);
        row.Children.Add(reset);
        footer.Children.Add(row);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 8, 0, 0);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        try
        {
            _status.Text = "جاري قياس CPU/RAM وتصنيف العمليات…";
            var list = await _service.ScanAsync();
            _grid.ItemsSource = list;
            var safe = list.Count(x => x.Decision == BackgroundProcessDecision.SafeToClose);
            var review = list.Count(x => x.Decision == BackgroundProcessDecision.Review);
            var protectedCount = list.Count(x => x.Decision == BackgroundProcessDecision.Protected);
            _status.Text = $"العمليات الظاهرة: {list.Count} • آمنة للإغلاق: {safe} • تحتاج مراجعة: {review} • محمية: {protectedCount}";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async Task SmartCleanAsync()
    {
        if (MessageBox.Show("D7 سيغلق فقط العمليات المصنفة Safe-To-Close والتي لا تملك نافذة ظاهرة. المتابعة؟", "D7 Smart Clean", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        SetBusy(true);
        try { _status.Text = await _service.SmartCleanAsync(); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { SetBusy(false); }
        await RefreshAsync();
    }

    private async Task CloseSelectedAsync()
    {
        if (_grid.SelectedItem is not BackgroundProcessRecord item) { _status.Text = "اختر عملية أولًا."; return; }
        if (!item.CanClose) { _status.Text = $"D7 رفض الإغلاق: {item.Reason}"; return; }
        var allowReview = item.Decision == BackgroundProcessDecision.Review;
        if (allowReview && MessageBox.Show($"{item.Name} ليس Safe-To-Close تلقائيًا. إغلاقه قد يوقف وظيفة تستخدمها. هل تريد إغلاقه يدويًا؟", "مراجعة مطلوبة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        SetBusy(true);
        try { _status.Text = await _service.CloseAsync(item.ProcessId, allowReview); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { SetBusy(false); }
        await RefreshAsync();
    }

    private void SetPolicy(bool alwaysClose)
    {
        if (_grid.SelectedItem is not BackgroundProcessRecord item) { _status.Text = "اختر عملية أولًا."; return; }
        if (!item.CanClose && alwaysClose)
        {
            _status.Text = "لا يمكن تحويل عملية Windows/حماية أساسية إلى Always Close.";
            return;
        }
        _status.Text = _service.SetPolicy(item, alwaysClose);
        _ = RefreshAsync();
    }

    private void ResetPolicy()
    {
        if (_grid.SelectedItem is not BackgroundProcessRecord item) { _status.Text = "اختر عملية أولًا."; return; }
        _status.Text = _service.ClearPolicy(item);
        _ = RefreshAsync();
    }

    private void SetBusy(bool busy)
    {
        _refresh.IsEnabled = !busy;
        _smartClean.IsEnabled = !busy;
    }

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
