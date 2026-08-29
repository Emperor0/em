using D7SystemIntelligence.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class DriverSafetyWindow : Window
{
    private readonly DriverSafetyService _service = new();
    private readonly DataGrid _updates = new();
    private readonly ComboBox _backups = new();
    private readonly TextBlock _status = new();

    public DriverSafetyWindow()
    {
        Title = "D7 — Driver Safety Center";
        Width = 1020;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Driver Safety Center", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "لا يطارد أحدث رقم فقط. هذا القسم يعمل بمصدر Windows Update الرسمي، ويأخذ Driver Store Backup قبل أي تثبيت. تعريفات NVIDIA/AMD الرسمية المنفصلة ستبقى مسارًا مستقلاً حتى نربط مصدر الشركة والتحقق/benchmark.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10)
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var backup = new Button { Content = "Backup Driver Store" };
        backup.Click += async (_, _) => await RunBusyAsync(backup, async () => (await _service.BackupDriverStoreAsync()).Detail);
        var scan = new Button { Content = "فحص Driver Updates" };
        scan.Click += async (_, _) => await ScanUpdatesAsync(scan);
        var install = new Button { Content = "تثبيت تحديثات Windows Driver" };
        install.Click += async (_, _) =>
        {
            if (MessageBox.Show("D7 سيصدر Driver Store كامل أولًا ثم يثبت Driver Updates المتاحة من Windows Update. قد تحتاج إعادة تشغيل. متابعة؟", "D7 Driver Update", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunBusyAsync(install, async () =>
            {
                var r = await _service.InstallWindowsUpdateDriversAsync();
                return r.Detail + (r.RebootRequired ? "\n\nWindows يطلب إعادة تشغيل لإكمال التعريفات." : string.Empty);
            });
            await ScanUpdatesAsync(scan);
        };
        actions.Children.Add(backup); actions.Children.Add(scan); actions.Children.Add(install);
        header.Children.Add(actions);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _updates.AutoGenerateColumns = false;
        _updates.IsReadOnly = true;
        _updates.Margin = new Thickness(0, 14, 0, 12);
        _updates.Columns.Add(new DataGridTextColumn { Header = "Driver Update", Binding = new System.Windows.Data.Binding(nameof(WindowsDriverUpdate.Title)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _updates.Columns.Add(new DataGridCheckBoxColumn { Header = "Downloaded", Binding = new System.Windows.Data.Binding(nameof(WindowsDriverUpdate.Downloaded)), Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _updates.Columns.Add(new DataGridTextColumn { Header = "الوصف", Binding = new System.Windows.Data.Binding(nameof(WindowsDriverUpdate.Description)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        Grid.SetRow(_updates, 1);
        root.Children.Add(_updates);

        var footer = new StackPanel();
        footer.Children.Add(new TextBlock { Text = "Restore Vault", FontSize = 18, FontWeight = FontWeights.SemiBold });
        _backups.Margin = new Thickness(0, 6, 0, 6);
        footer.Children.Add(_backups);
        var restoreRow = new StackPanel { Orientation = Orientation.Horizontal };
        var refreshBackups = new Button { Content = "تحديث قائمة Backup" };
        refreshBackups.Click += (_, _) => RefreshBackups();
        var open = new Button { Content = "فتح المجلد" };
        open.Click += (_, _) =>
        {
            if (_backups.SelectedItem is string path && Directory.Exists(path))
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        };
        var restore = new Button { Content = "إعادة إضافة Backup للتعريفات" };
        restore.Click += async (_, _) =>
        {
            if (_backups.SelectedItem is not string path) { _status.Text = "اختر Backup أولًا."; return; }
            if (MessageBox.Show("سيتم إعادة إضافة INF المحفوظة إلى Driver Store وطلب تثبيت الأنسب. Windows قد يمنع downgrade حسب Driver Ranking. متابعة؟", "استعادة Drivers", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunBusyAsync(restore, async () => (await _service.RestoreExportedDriversAsync(path)).Detail);
        };
        restoreRow.Children.Add(refreshBackups); restoreRow.Children.Add(open); restoreRow.Children.Add(restore);
        footer.Children.Add(restoreRow);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 8, 0, 0);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Loaded += async (_, _) => { RefreshBackups(); await ScanUpdatesAsync(scan); };
    }

    private async Task ScanUpdatesAsync(Button button)
    {
        button.IsEnabled = false;
        _status.Text = "جاري سؤال Windows Update عن Driver Updates…";
        try
        {
            var list = await _service.ScanWindowsUpdateDriversAsync();
            _updates.ItemsSource = list;
            _status.Text = list.Count == 0 ? "Windows Update لا يعرض Driver Updates حاليًا." : $"Windows Update يعرض {list.Count} Driver Update.";
        }
        catch (Exception ex) { _status.Text = "Driver Scan: " + ex.Message; }
        finally { button.IsEnabled = true; }
    }

    private async Task RunBusyAsync(Button button, Func<Task<string>> action)
    {
        button.IsEnabled = false;
        try { _status.Text = await action(); RefreshBackups(); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { button.IsEnabled = true; }
    }

    private void RefreshBackups()
    {
        var list = _service.ListBackups();
        _backups.ItemsSource = list;
        if (list.Count > 0) _backups.SelectedIndex = 0;
    }

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
