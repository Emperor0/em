using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class DisplayControlWindow : Window
{
    private readonly DisplayControlService _service = new();
    private readonly TextBlock _status = new();
    private readonly ComboBox _modes = new();
    private readonly Slider _brightness = new();
    private readonly TextBlock _brightnessText = new();

    public DisplayControlWindow()
    {
        Title = "D7 — Display Intelligence";
        Width = 720;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "ذكاء وتحكم الشاشة", FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "تغيير Refresh Rate يتم عبر Windows بعد اختبار الوضع أولًا. السطوع يستخدم DDC/CI فقط إذا الشاشة تدعمه.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 15)
        });

        var refreshCard = Card();
        var refreshStack = new StackPanel();
        refreshStack.Children.Add(new TextBlock { Text = "Refresh Rate", FontSize = 20, FontWeight = FontWeights.SemiBold });
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 6, 0, 10);
        refreshStack.Children.Add(_status);
        _modes.Margin = new Thickness(0, 0, 0, 10);
        refreshStack.Children.Add(_modes);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var apply = new Button { Content = "تطبيق المحدد" };
        apply.Click += (_, _) => ApplySelected();
        var max = new Button { Content = "أقصى Hz مدعوم" };
        max.Click += (_, _) => { _status.Text = _service.ApplyMaximumRefresh(); ReloadModes(); };
        var restore = new Button { Content = "استعادة السابق" };
        restore.Click += (_, _) => { _status.Text = _service.Restore(); ReloadModes(); };
        row.Children.Add(apply); row.Children.Add(max); row.Children.Add(restore);
        refreshStack.Children.Add(row);
        refreshCard.Child = refreshStack;
        root.Children.Add(refreshCard);

        var brightnessCard = Card();
        var bStack = new StackPanel();
        bStack.Children.Add(new TextBlock { Text = "DDC/CI Brightness", FontSize = 20, FontWeight = FontWeights.SemiBold });
        _brightnessText.Margin = new Thickness(0, 6, 0, 8);
        _brightnessText.TextWrapping = TextWrapping.Wrap;
        bStack.Children.Add(_brightnessText);
        _brightness.Minimum = 0;
        _brightness.Maximum = 100;
        _brightness.TickFrequency = 5;
        _brightness.IsSnapToTickEnabled = false;
        bStack.Children.Add(_brightness);
        var setBrightness = new Button { Content = "تطبيق السطوع", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        setBrightness.Click += (_, _) =>
        {
            _brightnessText.Text = _service.SetBrightness((uint)Math.Round(_brightness.Value));
            ReloadBrightness();
        };
        bStack.Children.Add(setBrightness);
        brightnessCard.Child = bStack;
        root.Children.Add(brightnessCard);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { ReloadModes(); ReloadBrightness(); };
    }

    private void ReloadModes()
    {
        var current = _service.GetCurrentMode();
        var modes = _service.GetModesForCurrentResolution();
        _modes.ItemsSource = modes;
        _modes.SelectedItem = current == null ? null : modes.FirstOrDefault(x => x.RefreshRateHz == current.RefreshRateHz && x.BitsPerPixel == current.BitsPerPixel);
        _status.Text = current == null
            ? "تعذر قراءة وضع الشاشة."
            : $"الحالي: {current}. الخيارات المكتشفة على نفس الدقة: {modes.Count}.";
    }

    private void ReloadBrightness()
    {
        var info = _service.ReadBrightness();
        _brightness.IsEnabled = info.Supported;
        if (info.Supported)
        {
            _brightness.Minimum = info.Minimum;
            _brightness.Maximum = info.Maximum;
            _brightness.Value = info.Current;
            _brightnessText.Text = $"الحالي {info.Current} | {info.Detail}";
        }
        else
        {
            _brightnessText.Text = info.Detail;
        }
    }

    private void ApplySelected()
    {
        if (_modes.SelectedItem is not DisplayModeInfo mode)
        {
            _status.Text = "اختر وضعًا أولًا.";
            return;
        }
        _status.Text = _service.ApplyRefreshRate(mode.RefreshRateHz);
        ReloadModes();
    }

    private Border Card() => new()
    {
        Background = Brush("Panel", Brushes.DimGray),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(15),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
