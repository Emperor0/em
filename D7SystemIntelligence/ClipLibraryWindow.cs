using D7SystemIntelligence.Core;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class ClipLibraryWindow : Window
{
    private readonly ClipLibraryService _clips = new();
    private readonly Func<string> _folderProvider;
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();

    public ClipLibraryWindow(Func<string> folderProvider)
    {
        _folderProvider = folderProvider;
        Title = "D7 NEXUS • Clip Library";
        Width = 1080;
        Height = 720;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "CLIP LIBRARY", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "إدارة مقاطع D7 بدون برنامج خارجي: فتح، إعادة تسمية، نقل، حذف وقص سريع بدون إعادة ترميز.", Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 16, 0, 12) };
        toolbar.Children.Add(Button("تحديث", async () => await ReloadAsync()));
        toolbar.Children.Add(Button("فتح موقع المقطع", () => { if (Selected() is { } c) SetStatus(_clips.Reveal(c.FullPath)); }));
        toolbar.Children.Add(Button("إعادة تسمية", async () => await RenameAsync()));
        toolbar.Children.Add(Button("نقل", async () => await MoveAsync()));
        toolbar.Children.Add(Button("قص سريع", async () => await TrimAsync()));
        toolbar.Children.Add(Button("حذف", async () => await DeleteAsync()));
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.Columns.Add(new DataGridTextColumn { Header = "المقطع", Binding = new System.Windows.Data.Binding(nameof(ClipRecord.FileName)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الحجم MB", Binding = new System.Windows.Data.Binding(nameof(ClipRecord.SizeMb)) { StringFormat = "0.0" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "التاريخ", Binding = new System.Windows.Data.Binding(nameof(ClipRecord.LastWriteTime)) { StringFormat = "yyyy-MM-dd HH:mm:ss" }, Width = new DataGridLength(1.7, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "المدة", Binding = new System.Windows.Data.Binding(nameof(ClipRecord.DurationSeconds)) { StringFormat = "0.0 s" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 2); root.Children.Add(_grid);

        _status.Text = "جاهز.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = (Brush)Application.Current.FindResource("Muted");
        _status.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_status, 3); root.Children.Add(_status);
        return root;
    }

    private Button Button(string text, Action action)
    {
        var b = new Button { Content = text, MinWidth = 110 };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } };
        return b;
    }

    private Button Button(string text, Func<Task> action)
    {
        var b = new Button { Content = text, MinWidth = 110 };
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            try { await action(); } catch (Exception ex) { SetStatus(ex.Message); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    private async Task ReloadAsync()
    {
        var folder = _folderProvider();
        Directory.CreateDirectory(folder);
        SetStatus("جاري قراءة المقاطع…");
        var list = await _clips.ScanAsync(folder, readDurations: true);
        _grid.ItemsSource = list;
        SetStatus($"{list.Count} مقطع • {list.Sum(x => x.SizeMb):0.0} MB • {folder}");
    }

    private ClipRecord? Selected()
    {
        if (_grid.SelectedItem is ClipRecord c) return c;
        SetStatus("اختر مقطعًا أولًا.");
        return null;
    }

    private async Task RenameAsync()
    {
        var clip = Selected(); if (clip == null) return;
        var value = Prompt("إعادة تسمية", "اسم المقطع الجديد بدون الامتداد:", Path.GetFileNameWithoutExtension(clip.FileName));
        if (value == null) return;
        SetStatus(_clips.Rename(clip.FullPath, value));
        await ReloadAsync();
    }

    private async Task MoveAsync()
    {
        var clip = Selected(); if (clip == null) return;
        var dialog = new OpenFolderDialog { Title = "اختر مجلد نقل المقطع", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        SetStatus(_clips.Move(clip.FullPath, dialog.FolderName));
        await ReloadAsync();
    }

    private async Task DeleteAsync()
    {
        var clip = Selected(); if (clip == null) return;
        if (MessageBox.Show($"حذف {clip.FileName} نهائيًا؟", "D7 Clip Library", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetStatus(_clips.Delete(clip.FullPath));
        await ReloadAsync();
    }

    private async Task TrimAsync()
    {
        var clip = Selected(); if (clip == null) return;
        var startText = Prompt("قص سريع", "ابدأ من الثانية:", "0");
        if (startText == null) return;
        var durationText = Prompt("قص سريع", "مدة النسخة الجديدة بالثواني:", "30");
        if (durationText == null) return;
        if (!double.TryParse(startText, out var start) || !double.TryParse(durationText, out var duration) || start < 0 || duration <= 0)
        {
            SetStatus("قيم القص غير صحيحة."); return;
        }
        var progress = new Progress<double>(p => SetStatus($"تجهيز/قص المقطع… {p:0}%"));
        SetStatus(await _clips.TrimFastAsync(clip.FullPath, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(duration), progress));
        await ReloadAsync();
    }

    private string? Prompt(string title, string label, string value)
    {
        var dialog = new Window
        {
            Title = title, Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, Background = (Brush)Application.Current.FindResource("Bg")
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        var box = new TextBox { Text = value }; root.Children.Add(box);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "تم", MinWidth = 90, IsDefault = true };
        var cancel = new Button { Content = "إلغاء", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        row.Children.Add(ok); row.Children.Add(cancel); root.Children.Add(row); dialog.Content = root;
        box.SelectAll(); box.Focus();
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private void SetStatus(string text) => _status.Text = text;
}
