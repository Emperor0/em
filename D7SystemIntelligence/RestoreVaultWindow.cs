using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class RestoreVaultWindow : Window
{
    private readonly RestoreVaultService _service = new();
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();

    public RestoreVaultWindow()
    {
        Title = "D7 NEXUS • Restore Vault";
        Width = 1020;
        Height = 680;
        MinWidth = 860;
        MinHeight = 560;
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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "RESTORE VAULT", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "النسخ التي أنشأها D7 قبل التغييرات القابلة للرجوع. الاستعادة تستخدم نفس المحرك الآمن بدل أوامر عشوائية.",
            Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 12) };
        var refresh = new Button { Content = "تحديث", MinWidth = 100 };
        refresh.Click += (_, _) => Reload();
        var reveal = new Button { Content = "فتح الموقع", MinWidth = 110 };
        reveal.Click += (_, _) => { if (Selected() is { } r) _status.Text = _service.Reveal(r); };
        var restore = new Button { Content = "استعادة المحدد", MinWidth = 135, Background = (Brush)Application.Current.FindResource("AccentSoft") };
        restore.Click += async (_, _) => await RestoreSelectedAsync(restore);
        row.Children.Add(refresh); row.Children.Add(reveal); row.Children.Add(restore);
        Grid.SetRow(row, 1); root.Children.Add(row);

        _grid.AutoGenerateColumns = false; _grid.IsReadOnly = true;
        _grid.Columns.Add(new DataGridTextColumn { Header = "النوع", Binding = new System.Windows.Data.Binding(nameof(RestoreVaultRecord.Type)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "النسخة", Binding = new System.Windows.Data.Binding(nameof(RestoreVaultRecord.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "التاريخ", Binding = new System.Windows.Data.Binding(nameof(RestoreVaultRecord.LastWriteTime)) { StringFormat = "yyyy-MM-dd HH:mm:ss" }, Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الحجم", Binding = new System.Windows.Data.Binding(nameof(RestoreVaultRecord.SizeBytes)) { StringFormat = "{0:N0} B" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الإجراء", Binding = new System.Windows.Data.Binding(nameof(RestoreVaultRecord.Action)), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 2); root.Children.Add(_grid);

        _status.TextWrapping = TextWrapping.Wrap; _status.Foreground = (Brush)Application.Current.FindResource("Muted"); _status.Margin = new Thickness(0, 12, 0, 0); _status.Text = "جاهز.";
        Grid.SetRow(_status, 3); root.Children.Add(_status);
        return root;
    }

    private void Reload()
    {
        var list = _service.Scan();
        _grid.ItemsSource = list;
        _status.Text = $"Restore Vault • {list.Count} نسخة/حالة • {_service.RootPath}";
        if (list.Count > 0) _grid.SelectedIndex = 0;
    }

    private RestoreVaultRecord? Selected()
    {
        if (_grid.SelectedItem is RestoreVaultRecord r) return r;
        _status.Text = "اختر نسخة أولًا.";
        return null;
    }

    private async Task RestoreSelectedAsync(Button button)
    {
        var record = Selected(); if (record == null) return;
        if (record.Action == "عرض فقط") { _status.Text = "هذه الحالة لا تملك Restore handler آمنًا."; return; }
        if (MessageBox.Show($"استعادة {record.Name}؟\nD7 سيستخدم Restore handler الخاص بـ{record.Type}.", "D7 Restore Vault", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        button.IsEnabled = false;
        try
        {
            _status.Text = "جاري الاستعادة…";
            var result = await _service.RestoreAsync(record);
            _status.Text = result.Detail;
            Reload();
        }
        catch (Exception ex) { _status.Text = "Restore Vault: " + ex.Message; }
        finally { button.IsEnabled = true; }
    }
}
