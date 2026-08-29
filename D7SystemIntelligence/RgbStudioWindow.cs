using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class RgbStudioWindow : Window
{
    private readonly ManagedOpenRgbService _rgb = new();
    private readonly TemperatureRgbController _temperature;
    private readonly TextBlock _status = new();
    private readonly TextBox _devices = new();
    private readonly TextBox _hex = new() { Text = "7A5CFF", Width = 120, FlowDirection = FlowDirection.LeftToRight };

    public RgbStudioWindow(HardwareEngine hardware)
    {
        _temperature = new TemperatureRgbController(hardware, _rgb);
        _temperature.StatusChanged += message => Dispatcher.Invoke(() => _status.Text = message);

        Title = "D7 — RGB Studio";
        Width = 820;
        Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "D7 RGB Studio", FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "يستخدم OpenRGB الرسمي للأجهزة المدعومة. إذا لم يكن موجودًا، D7 ينزل Windows 64 ZIP الرسمي ويتحقق من SHA-256 قبل تشغيله.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0, 6, 0, 12)
        });

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(_status);

        var prepare = new Button { Content = "تجهيز OpenRGB الرسمي", HorizontalAlignment = HorizontalAlignment.Right };
        prepare.Click += async (_, _) =>
        {
            prepare.IsEnabled = false;
            try
            {
                var progress = new Progress<double>(p => _status.Text = $"جاري تنزيل OpenRGB… {p:0}%");
                var info = await _rgb.EnsureAsync(progress);
                _status.Text = info.Detail;
                await RefreshDevicesAsync();
            }
            catch (Exception ex) { _status.Text = "OpenRGB: " + ex.Message; }
            finally { prepare.IsEnabled = true; }
        };
        root.Children.Add(prepare);

        _devices.Height = 180;
        _devices.Margin = new Thickness(0, 12, 0, 12);
        _devices.IsReadOnly = true;
        _devices.AcceptsReturn = true;
        _devices.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _devices.FlowDirection = FlowDirection.LeftToRight;
        root.Children.Add(_devices);

        var list = new Button { Content = "فحص أجهزة RGB", HorizontalAlignment = HorizontalAlignment.Right };
        list.Click += async (_, _) => await RefreshDevicesAsync();
        root.Children.Add(list);

        var colorCard = Card();
        var colorStack = new StackPanel();
        colorStack.Children.Add(new TextBlock { Text = "لون ثابت", FontSize = 19, FontWeight = FontWeights.SemiBold });
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        colorRow.Children.Add(_hex);
        var apply = new Button { Content = "تطبيق" };
        apply.Click += async (_, _) => _status.Text = await _rgb.SetColorAsync(_hex.Text);
        colorRow.Children.Add(apply);
        var off = new Button { Content = "إطفاء RGB" };
        off.Click += async (_, _) => _status.Text = await _rgb.TurnOffAsync();
        colorRow.Children.Add(off);
        colorStack.Children.Add(colorRow);

        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var preset in new[] { ("أحمر","FF0000"), ("أزرق","0066FF"), ("بنفسجي","7A5CFF"), ("أبيض","FFFFFF") })
        {
            var b = new Button { Content = preset.Item1 };
            b.Click += async (_, _) => { _hex.Text = preset.Item2; _status.Text = await _rgb.SetColorAsync(preset.Item2); };
            presets.Children.Add(b);
        }
        colorStack.Children.Add(presets);
        colorCard.Child = colorStack;
        root.Children.Add(colorCard);

        var tempCard = Card();
        var tempStack = new StackPanel();
        tempStack.Children.Add(new TextBlock { Text = "Temperature RGB", FontSize = 19, FontWeight = FontWeights.SemiBold });
        tempStack.Children.Add(new TextBlock
        {
            Text = "أخضر تحت 50° → أخضر فاتح → أصفر → برتقالي → أحمر عند 80°+. يتم إرسال أمر فقط عند تغير نطاق الحرارة لتجنب حمل غير ضروري.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0, 6, 0, 8)
        });
        var tempRow = new StackPanel { Orientation = Orientation.Horizontal };
        var start = new Button { Content = "تشغيل وضع الحرارة" };
        start.Click += (_, _) => _temperature.Start();
        var stop = new Button { Content = "إيقاف وضع الحرارة" };
        stop.Click += (_, _) => _temperature.Stop();
        tempRow.Children.Add(start); tempRow.Children.Add(stop);
        tempStack.Children.Add(tempRow);
        tempCard.Child = tempStack;
        root.Children.Add(tempCard);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += async (_, _) => { _status.Text = _rgb.Detect().Detail; if (_rgb.Detect().Available) await RefreshDevicesAsync(); };
        Closed += (_, _) => _temperature.Dispose();
    }

    private async Task RefreshDevicesAsync()
    {
        try { _devices.Text = await _rgb.ListDevicesAsync(); }
        catch (Exception ex) { _devices.Text = ex.Message; }
    }

    private Border Card() => new()
    {
        Background = Brush("Panel", Brushes.DimGray),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14),
        Margin = new Thickness(0, 12, 0, 0)
    };

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
