using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class SmartRemovalWindow : Window
{
    private readonly SmartRemovalService _service = new();
    private readonly DataGrid _apps = new();
    private readonly DataGrid _quarantine = new();
    private readonly TextBox _path = new();
    private readonly TextBlock _analysis = new();
    private readonly TextBlock _status = new();
    private RemovalAnalysis? _currentPathAnalysis;

    public SmartRemovalWindow()
    {
        Title = "D7 — الحذف الذكي من الجذور";
        Width = 1260;
        Height = 790;
        MinWidth = 1000;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Smart Root Remover", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "برنامج مثبت؟ D7 يشغل Uninstaller/MSI أولًا ثم يفحص البقايا. ملف أو مجلد؟ يمر على حماية Windows + المسارات الحساسة + الأقفال قبل أي حذف. الافتراضي Quarantine قابل للاستعادة، والحذف النهائي لا يُفتح إلا للهدف المصنف Safe.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Muted", Brushes.Gray),
            Margin = new Thickness(0, 6, 0, 12)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(BuildAppsTab());
        tabs.Items.Add(BuildPathTab());
        tabs.Items.Add(BuildQuarantineTab());
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        Content = root;
        Loaded += (_, _) =>
        {
            RefreshApps();
            RefreshQuarantine();
        };
    }

    private TabItem BuildAppsTab()
    {
        var tab = new TabItem { Header = "البرامج المثبتة" };
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = new Button { Content = "إعادة الفحص" };
        refresh.Click += (_, _) => RefreshApps();
        var analyze = new Button { Content = "تحليل المحدد" };
        analyze.Click += async (_, _) => await AnalyzeSelectedAppAsync();
        var uninstall = new Button { Content = "إزالة رسمية" };
        uninstall.Click += async (_, _) => await UninstallSelectedAsync(false);
        var deep = new Button { Content = "إزالة + Deep Cleanup" };
        deep.Click += async (_, _) => await UninstallSelectedAsync(true);
        buttons.Children.Add(refresh);
        buttons.Children.Add(analyze);
        buttons.Children.Add(uninstall);
        buttons.Children.Add(deep);
        Grid.SetRow(buttons, 0);
        grid.Children.Add(buttons);

        _apps.AutoGenerateColumns = false;
        _apps.IsReadOnly = true;
        _apps.SelectionMode = DataGridSelectionMode.Single;
        _apps.Margin = new Thickness(0, 10, 0, 10);
        _apps.Columns.Add(new DataGridTextColumn { Header = "البرنامج", Binding = new System.Windows.Data.Binding(nameof(InstalledAppRecord.DisplayName)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _apps.Columns.Add(new DataGridTextColumn { Header = "الإصدار", Binding = new System.Windows.Data.Binding(nameof(InstalledAppRecord.DisplayVersion)), Width = new DataGridLength(.8, DataGridLengthUnitType.Star) });
        _apps.Columns.Add(new DataGridTextColumn { Header = "الناشر", Binding = new System.Windows.Data.Binding(nameof(InstalledAppRecord.Publisher)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _apps.Columns.Add(new DataGridTextColumn { Header = "Install Location", Binding = new System.Windows.Data.Binding(nameof(InstalledAppRecord.InstallLocation)), Width = new DataGridLength(2.5, DataGridLengthUnitType.Star) });
        _apps.Columns.Add(new DataGridCheckBoxColumn { Header = "MSI", Binding = new System.Windows.Data.Binding(nameof(InstalledAppRecord.WindowsInstaller)), Width = new DataGridLength(.45, DataGridLengthUnitType.Star) });
        Grid.SetRow(_apps, 1);
        grid.Children.Add(_apps);

        var note = new TextBlock
        {
            Text = "Deep Cleanup لا يمس مجلدًا مشتركًا بين أكثر من برنامج. إذا لم يثبت D7 أن InstallLocation خاص بالبرنامج وحده، سيترك البقايا للمراجعة بدل حذفها.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Muted", Brushes.Gray)
        };
        Grid.SetRow(note, 2);
        grid.Children.Add(note);
        tab.Content = grid;
        return tab;
    }

    private TabItem BuildPathTab()
    {
        var tab = new TabItem { Header = "ملف / مجلد" };
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "اكتب المسار الكامل للملف أو المجلد:", FontWeight = FontWeights.SemiBold });
        _path.Margin = new Thickness(0, 6, 0, 10);
        _path.MinHeight = 34;
        stack.Children.Add(_path);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var analyze = new Button { Content = "حلل قبل الحذف" };
        analyze.Click += async (_, _) => await AnalyzePathAsync();
        var quarantine = new Button { Content = "إزالة إلى Quarantine" };
        quarantine.Click += async (_, _) => await QuarantineCurrentAsync();
        var permanent = new Button { Content = "حذف نهائي — Safe فقط" };
        permanent.Click += async (_, _) => await PermanentlyDeleteCurrentAsync();
        buttons.Children.Add(analyze);
        buttons.Children.Add(quarantine);
        buttons.Children.Add(permanent);
        stack.Children.Add(buttons);

        _analysis.Margin = new Thickness(0, 14, 0, 0);
        _analysis.TextWrapping = TextWrapping.Wrap;
        _analysis.FontSize = 14;
        stack.Children.Add(_analysis);
        tab.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return tab;
    }

    private TabItem BuildQuarantineTab()
    {
        var tab = new TabItem { Header = "Quarantine / Restore" };
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _quarantine.AutoGenerateColumns = false;
        _quarantine.IsReadOnly = true;
        _quarantine.SelectionMode = DataGridSelectionMode.Single;
        _quarantine.Columns.Add(new DataGridTextColumn { Header = "التاريخ UTC", Binding = new System.Windows.Data.Binding(nameof(QuarantineRecord.CreatedUtc)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _quarantine.Columns.Add(new DataGridTextColumn { Header = "النوع", Binding = new System.Windows.Data.Binding(nameof(QuarantineRecord.TargetType)), Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _quarantine.Columns.Add(new DataGridTextColumn { Header = "المسار الأصلي", Binding = new System.Windows.Data.Binding(nameof(QuarantineRecord.OriginalPath)), Width = new DataGridLength(2.4, DataGridLengthUnitType.Star) });
        _quarantine.Columns.Add(new DataGridTextColumn { Header = "مكان Quarantine", Binding = new System.Windows.Data.Binding(nameof(QuarantineRecord.QuarantinedPath)), Width = new DataGridLength(2.4, DataGridLengthUnitType.Star) });
        Grid.SetRow(_quarantine, 0);
        grid.Children.Add(_quarantine);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var refresh = new Button { Content = "تحديث" };
        refresh.Click += (_, _) => RefreshQuarantine();
        var restore = new Button { Content = "استعادة المحدد" };
        restore.Click += (_, _) => RestoreSelected();
        var purge = new Button { Content = "حذف Quarantine نهائيًا" };
        purge.Click += (_, _) => PurgeSelected();
        row.Children.Add(refresh);
        row.Children.Add(restore);
        row.Children.Add(purge);
        Grid.SetRow(row, 1);
        grid.Children.Add(row);
        tab.Content = grid;
        return tab;
    }

    private void RefreshApps()
    {
        try
        {
            var list = _service.ScanInstalledApps();
            _apps.ItemsSource = list;
            _status.Text = $"تم العثور على {list.Count} برنامجًا مسجلًا في Windows Uninstall registry.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private async Task AnalyzeSelectedAppAsync()
    {
        if (_apps.SelectedItem is not InstalledAppRecord app) { _status.Text = "اختر برنامجًا أولًا."; return; }
        var result = await _service.AnalyzeInstalledAppAsync(app);
        _status.Text = FormatAnalysis(result);
    }

    private async Task UninstallSelectedAsync(bool deep)
    {
        if (_apps.SelectedItem is not InstalledAppRecord app) { _status.Text = "اختر برنامجًا أولًا."; return; }
        var analysis = await _service.AnalyzeInstalledAppAsync(app);
        if (analysis.Verdict == RemovalVerdict.Blocked)
        {
            _status.Text = FormatAnalysis(analysis);
            return;
        }
        var text = deep
            ? $"إزالة {app.DisplayName} رسميًا ثم محاولة نقل InstallLocation المتبقي إلى Quarantine فقط إذا أثبت D7 أنه مجلد حصري وآمن؟"
            : $"تشغيل أداة الإزالة الرسمية لـ {app.DisplayName}؟";
        if (MessageBox.Show(text, "D7 Smart Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _status.Text = "جاري تنفيذ الإزالة…";
        _status.Text = await _service.UninstallAppAsync(app, deep);
        RefreshApps();
        RefreshQuarantine();
    }

    private async Task AnalyzePathAsync()
    {
        _currentPathAnalysis = await _service.AnalyzePathAsync(_path.Text);
        _analysis.Text = FormatAnalysis(_currentPathAnalysis);
        _status.Text = _currentPathAnalysis.VerdictText;
    }

    private async Task QuarantineCurrentAsync()
    {
        await EnsureCurrentAnalysisAsync();
        if (_currentPathAnalysis == null) return;
        if (!_currentPathAnalysis.CanRemove) { _status.Text = FormatAnalysis(_currentPathAnalysis); return; }
        if (MessageBox.Show($"إزالة هذا الهدف من مكانه الأصلي إلى D7 Quarantine؟\n{_currentPathAnalysis.TargetPath}", "Quarantine", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _status.Text = await _service.QuarantineAsync(_currentPathAnalysis);
        _currentPathAnalysis = null;
        RefreshQuarantine();
    }

    private async Task PermanentlyDeleteCurrentAsync()
    {
        await EnsureCurrentAnalysisAsync();
        if (_currentPathAnalysis == null) return;
        if (_currentPathAnalysis.Verdict != RemovalVerdict.Safe)
        {
            _status.Text = "D7 يسمح بالحذف النهائي فقط إذا كانت النتيجة Safe. استخدم Quarantine لبقية الحالات.";
            return;
        }
        if (MessageBox.Show($"حذف نهائي بدون Restore؟\n{_currentPathAnalysis.TargetPath}", "تأكيد حذف نهائي", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes) return;
        _status.Text = await _service.PermanentDeleteAsync(_currentPathAnalysis);
        _currentPathAnalysis = null;
    }

    private async Task EnsureCurrentAnalysisAsync()
    {
        if (_currentPathAnalysis == null || !_currentPathAnalysis.TargetPath.Equals(_path.Text.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase))
            await AnalyzePathAsync();
    }

    private void RefreshQuarantine()
    {
        try { _quarantine.ItemsSource = _service.ListQuarantine(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void RestoreSelected()
    {
        if (_quarantine.SelectedItem is not QuarantineRecord item) { _status.Text = "اختر عنصر Quarantine."; return; }
        _status.Text = _service.RestoreQuarantine(item.Id);
        RefreshQuarantine();
    }

    private void PurgeSelected()
    {
        if (_quarantine.SelectedItem is not QuarantineRecord item) { _status.Text = "اختر عنصر Quarantine."; return; }
        if (MessageBox.Show("سيتم حذف نسخة Quarantine نهائيًا ولن يمكن استعادتها. متابعة؟", "Purge", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes) return;
        _status.Text = _service.PurgeQuarantine(item.Id);
        RefreshQuarantine();
    }

    private static string FormatAnalysis(RemovalAnalysis analysis)
    {
        var locks = analysis.LockingProcesses.Count == 0 ? "لا يوجد قفل معروف" : "مستخدم بواسطة: " + string.Join("، ", analysis.LockingProcesses);
        return $"الحكم: {analysis.VerdictText}\nالهدف: {analysis.TargetPath}\nالنوع: {analysis.TargetType}\n{locks}\n\n" + string.Join(Environment.NewLine, analysis.Reasons.Select(x => "• " + x));
    }

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
