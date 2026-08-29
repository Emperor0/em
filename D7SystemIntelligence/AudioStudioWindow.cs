using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class AudioStudioWindow : Window
{
    private readonly AudioControlService _audio = new();
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Value = 70, Width = 240 };

    public AudioStudioWindow()
    {
        Title = "D7 — Audio Studio";
        Width = 1120;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "D7 Audio Studio", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "يعرض مخارج/مداخل Core Audio الفعلية مثل Astro وSteelSeries Sonar، الـDefault حسب الدور، Mix Format، مستوى الصوت والكتم. تبديل Default يحفظ السابق أول مرة للاستعادة.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10)
        });
        var scan = new Button { Content = "إعادة فحص الصوت", HorizontalAlignment = HorizontalAlignment.Right };
        scan.Click += (_, _) => Refresh();
        header.Children.Add(scan);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.Margin = new Thickness(0, 14, 0, 12);
        _grid.Columns.Add(new DataGridTextColumn { Header = "النوع", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.Direction)), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الجهاز", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.Name)), Width = new DataGridLength(2.5, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Game", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.IsDefaultMultimedia)), Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Comm", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.IsDefaultCommunications)), Width = new DataGridLength(.6, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Volume", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.VolumePercent)) { StringFormat = "{0:0}%" }, Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Mute", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.Muted)), Width = new DataGridLength(.5, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Hz", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.SampleRate)), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Ch", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.Channels)), Width = new DataGridLength(.4, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Bit", Binding = new System.Windows.Data.Binding(nameof(AudioEndpointRecord.BitsPerSample)), Width = new DataGridLength(.4, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) =>
        {
            if (_grid.SelectedItem is AudioEndpointRecord item) _volume.Value = item.VolumePercent;
        };
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var footer = new StackPanel();
        var volumeRow = new StackPanel { Orientation = Orientation.Horizontal };
        volumeRow.Children.Add(new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        volumeRow.Children.Add(_volume);
        var setVolume = new Button { Content = "تطبيق الصوت" };
        setVolume.Click += (_, _) => WithSelected(x => _audio.SetVolume(x.Id, (float)_volume.Value));
        var mute = new Button { Content = "Mute" };
        mute.Click += (_, _) => WithSelected(x => _audio.SetMute(x.Id, true));
        var unmute = new Button { Content = "Unmute" };
        unmute.Click += (_, _) => WithSelected(x => _audio.SetMute(x.Id, false));
        volumeRow.Children.Add(setVolume); volumeRow.Children.Add(mute); volumeRow.Children.Add(unmute);
        footer.Children.Add(volumeRow);

        var defaultsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var gameDefault = new Button { Content = "اجعله Game/Desktop Default" };
        gameDefault.Click += (_, _) => WithSelected(x => _audio.SetDefault(x.Id, false));
        var commDefault = new Button { Content = "اجعله Communications Default" };
        commDefault.Click += (_, _) => WithSelected(x => _audio.SetDefault(x.Id, true));
        var save = new Button { Content = "حفظ Defaults الحالية" };
        save.Click += (_, _) => { _status.Text = _audio.SaveCurrentDefaults(); Refresh(); };
        var restore = new Button { Content = "استعادة Defaults" };
        restore.Click += (_, _) => { _status.Text = _audio.RestoreDefaults(); Refresh(); };
        defaultsRow.Children.Add(gameDefault); defaultsRow.Children.Add(commDefault); defaultsRow.Children.Add(save); defaultsRow.Children.Add(restore);
        footer.Children.Add(defaultsRow);

        _status.Text = "اختر جهازًا لتنفيذ التحكم.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 9, 0, 0);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Loaded += (_, _) => Refresh();
    }

    private void WithSelected(Func<AudioEndpointRecord, string> action)
    {
        if (_grid.SelectedItem is not AudioEndpointRecord item)
        {
            _status.Text = "اختر جهاز صوت أولًا.";
            return;
        }
        try { _status.Text = action(item); }
        catch (Exception ex) { _status.Text = "Audio: " + ex.Message; }
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            var list = _audio.Scan();
            _grid.ItemsSource = list;
            if (list.Count == 0) _status.Text = "لم يتم العثور على Audio Endpoints نشطة.";
        }
        catch (Exception ex) { _status.Text = "تعذر فحص الصوت: " + ex.Message; }
    }

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
