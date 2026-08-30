using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class RgbStudioWindow : Window
{
    private sealed class DeviceEditor
    {
        public required OpenRgbDevice Device { get; init; }
        public required ComboBox Mode { get; init; }
        public required TextBox Hex { get; init; }
        public required Slider Brightness { get; init; }
        public required Border Preview { get; init; }
        public required CheckBox Enabled { get; init; }
    }

    private readonly HardwareEngine _hardware;
    private readonly Func<string?> _gameProvider;
    private readonly Func<D7Mission> _missionProvider;
    private readonly ManagedOpenRgbService _rgb = new();
    private readonly TemperatureRgbController _temperature;
    private readonly RgbSceneStore _scenes = new();
    private readonly StackPanel _deviceRows = new();
    private readonly List<DeviceEditor> _editors = [];
    private readonly TextBlock _status = new();
    private readonly TextBlock _intelligenceState = new();
    private readonly ComboBox _intelligenceMode = new() { MinWidth = 210 };
    private readonly ComboBox _sceneList = new() { MinWidth = 180 };
    private readonly TextBox _sceneName = new() { MinWidth = 180, Text = "D7KT Scene" };
    private readonly DispatcherTimer _intelligenceTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? _lastIntelligenceSignature;
    private bool _intelligenceBusy;

    public RgbStudioWindow(HardwareEngine hardware, Func<string?>? gameProvider = null, Func<D7Mission>? missionProvider = null)
    {
        _hardware = hardware;
        _gameProvider = gameProvider ?? (() => D7RuntimeBus.Context?.PrimaryGame);
        _missionProvider = missionProvider ?? (() => D7RuntimeBus.Mission);
        _temperature = new TemperatureRgbController(hardware, _rgb);
        _temperature.StatusChanged += message => Dispatcher.Invoke(() => _status.Text = message);

        Title = "D7KT — RGB Intelligence Studio";
        Width = 1140;
        Height = 820;
        MinWidth = 920;
        MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        Content = BuildUi();
        _intelligenceTimer.Tick += async (_, _) => await IntelligenceTickAsync();
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _intelligenceTimer.Stop();
            _temperature.Dispose();
        };
    }

    private UIElement BuildUi()
    {
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = "RGB Intelligence Studio", FontSize = 30, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "تحكم مستقل بكل جهاز + Scenes + Mode/Brightness + ربط الإضاءة بحرارة الجهاز والحمل واللعبة والـMission. ما يدعمه الهاردوير فقط — بدون أزرار وهمية.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0, 6, 0, 12)
        });

        root.Children.Add(BackendCard());
        root.Children.Add(DeviceMatrixCard());
        root.Children.Add(SceneCard());
        root.Children.Add(IntelligenceCard());
        root.Children.Add(AdvancedCard());

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private Border BackendCard()
    {
        var card = Card();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "OpenRGB Hardware Backend", FontSize = 18, FontWeight = FontWeights.SemiBold });
        _status.Foreground = Brush("Muted", Brushes.Gray);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 6, 0, 0);
        text.Children.Add(_status);
        grid.Children.Add(text);
        var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(ActionButton("تجهيز / تحديث Backend", PrepareAsync));
        buttons.Children.Add(ActionButton("Advanced Studio", () =>
        {
            _status.Text = _rgb.LaunchAdvancedStudio();
            return Task.CompletedTask;
        }, true));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        card.Child = grid;
        return card;
    }

    private Border DeviceMatrixCard()
    {
        var card = Card();
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = "Device Matrix", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var refresh = ActionButton("إعادة فحص الأجهزة", RefreshDevicesAsync);
        Grid.SetColumn(refresh, 1);
        head.Children.Add(refresh);
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock
        {
            Text = "كل جهاز مستقل. مثال: الكيبورد أحمر، الماوس أبيض، الرام بنفسجي، والمراوح Mode مختلف — إذا OpenRGB يعلن أن الجهاز يدعمه.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10)
        });
        _deviceRows.Children.Add(Empty("جاري اكتشاف أجهزة RGB…"));
        stack.Children.Add(_deviceRows);
        card.Child = stack;
        return card;
    }

    private Border SceneCard()
    {
        var card = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "D7KT Scenes", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "Scene تحفظ لون/Mode/Brightness لكل جهاز. مثال: Ranked / Desktop / Horror / Stream.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        var row = new WrapPanel();
        row.Children.Add(_sceneName);
        row.Children.Add(ActionButton("حفظ الحالية", SaveSceneAsync, true));
        row.Children.Add(_sceneList);
        row.Children.Add(ActionButton("تحميل وتطبيق", LoadSceneAsync));
        row.Children.Add(ActionButton("حذف", DeleteSceneAsync));
        stack.Children.Add(row);
        card.Child = stack;
        return card;
    }

    private Border IntelligenceCard()
    {
        var card = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "RGB Intelligence", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "هذه ميزة D7KT الحقيقية فوق برامج RGB: الإضاءة تعرف حالة الجهاز واللعبة والـMission. لا يرسل أمر جديد إلا عندما تتغير الحالة.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        foreach (var item in new[] { "Off / Manual", "Temperature Guard", "Performance Load", "Game Presence", "Mission Sync" })
            _intelligenceMode.Items.Add(item);
        _intelligenceMode.SelectedIndex = 0;
        _intelligenceMode.SelectionChanged += (_, _) => ConfigureIntelligence();
        var row = new WrapPanel();
        row.Children.Add(_intelligenceMode);
        row.Children.Add(ActionButton("تطبيق Manual على الكل", ApplyAllManualAsync, true));
        row.Children.Add(ActionButton("إطفاء الكل", async () => _status.Text = await _rgb.TurnOffAsync()));
        stack.Children.Add(row);
        _intelligenceState.Text = "Manual: كل جهاز يعمل بإعداد صفه.";
        _intelligenceState.Foreground = Brush("Muted", Brushes.Gray);
        _intelligenceState.TextWrapping = TextWrapping.Wrap;
        _intelligenceState.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_intelligenceState);
        card.Child = stack;
        return card;
    }

    private Border AdvancedCard()
    {
        var card = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Zones / Per‑LED / Effects", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "للـZones وPer‑LED وVisual Map وEffects Plugins نستخدم OpenRGB Advanced Studio بدل بناء نسخة ناقصة منه. D7KT يضيف فوقه الـAutomation والScenes وربط الأداء واللعبة. إذا الجهاز لا يعلن هذه القدرات فلن ندعي دعمها.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8)
        });
        stack.Children.Add(ActionButton("فتح Advanced Studio", () =>
        {
            _status.Text = _rgb.LaunchAdvancedStudio();
            return Task.CompletedTask;
        }, true));
        card.Child = stack;
        return card;
    }

    private async Task InitializeAsync()
    {
        _status.Text = _rgb.Detect().Detail;
        RefreshScenes();
        if (_rgb.Detect().Available) await RefreshDevicesAsync();
        else
        {
            _deviceRows.Children.Clear();
            _deviceRows.Children.Add(Empty("Backend غير مجهز. اضغط تجهيز / تحديث Backend أولًا."));
        }
    }

    private async Task PrepareAsync()
    {
        var progress = new Progress<double>(p => _status.Text = $"جاري تجهيز OpenRGB الرسمي… {p:0}%");
        var result = await _rgb.EnsureAsync(progress);
        _status.Text = result.Detail;
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        _deviceRows.Children.Clear();
        _deviceRows.Children.Add(Empty("جاري قراءة الأجهزة والمودات…"));
        _editors.Clear();
        try
        {
            var devices = await _rgb.GetDevicesAsync();
            _deviceRows.Children.Clear();
            if (devices.Count == 0)
            {
                _deviceRows.Children.Add(Empty("OpenRGB لم يكتشف جهاز RGB مدعوم. D7KT لن يعرض تحكمًا وهميًا."));
                return;
            }
            foreach (var device in devices)
            {
                var editor = CreateEditor(device);
                _editors.Add(editor);
                _deviceRows.Children.Add(DeviceRow(editor));
            }
            _status.Text = $"تم اكتشاف {devices.Count} جهاز RGB • التحكم مستقل لكل جهاز.";
        }
        catch (Exception ex)
        {
            _deviceRows.Children.Clear();
            _deviceRows.Children.Add(Empty("فشل اكتشاف RGB: " + ex.Message));
        }
    }

    private DeviceEditor CreateEditor(OpenRgbDevice device)
    {
        var modes = new ComboBox { MinWidth = 145 };
        foreach (var item in (device.Modes.Count > 0 ? device.Modes : ["static"]).Distinct(StringComparer.OrdinalIgnoreCase))
            modes.Items.Add(item);
        if (!modes.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), "static", StringComparison.OrdinalIgnoreCase)))
            modes.Items.Insert(0, "static");
        modes.SelectedIndex = 0;
        var hex = new TextBox { Text = DefaultColor(device), Width = 92, FlowDirection = FlowDirection.LeftToRight };
        var preview = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(7), BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(1) };
        UpdatePreview(preview, hex.Text);
        hex.TextChanged += (_, _) => UpdatePreview(preview, hex.Text);
        return new DeviceEditor
        {
            Device = device,
            Mode = modes,
            Hex = hex,
            Brightness = new Slider { Minimum = 0, Maximum = 100, Value = 100, Width = 110 },
            Preview = preview,
            Enabled = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    private UIElement DeviceRow(DeviceEditor e)
    {
        var box = new Border
        {
            Background = Brush("Panel2", Brushes.DimGray), BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 4, 0, 4)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(e.Enabled);
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = $"#{e.Device.Index}  {e.Device.Name}", FontSize = 15, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight });
        info.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(e.Device.Type) ? "OpenRGB device" : e.Device.Type, Foreground = Brush("Muted", Brushes.Gray), FontSize = 10.5, FlowDirection = FlowDirection.LeftToRight });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        var controls = new WrapPanel { FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(e.Mode);
        controls.Children.Add(e.Preview);
        controls.Children.Add(e.Hex);
        controls.Children.Add(new TextBlock { Text = "Brightness", Foreground = Brush("Muted", Brushes.Gray), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 2, 0) });
        controls.Children.Add(e.Brightness);
        controls.Children.Add(ActionButton("تطبيق", async () => await ApplyEditorAsync(e), true));
        controls.Children.Add(ActionButton("إطفاء", async () => _status.Text = await _rgb.TurnOffDeviceAsync(e.Device.Index)));
        Grid.SetColumn(controls, 2);
        grid.Children.Add(controls);
        box.Child = grid;
        return box;
    }

    private async Task ApplyEditorAsync(DeviceEditor e)
    {
        if (e.Enabled.IsChecked != true) return;
        _status.Text = await _rgb.SetDeviceModeAsync(
            e.Device.Index,
            e.Mode.SelectedItem?.ToString() ?? "static",
            e.Hex.Text,
            (int)Math.Round(e.Brightness.Value));
    }

    private async Task ApplyAllManualAsync()
    {
        var count = 0;
        foreach (var e in _editors.Where(x => x.Enabled.IsChecked == true))
        {
            await ApplyEditorAsync(e);
            count++;
        }
        _status.Text = count == 0 ? "لا يوجد جهاز مفعّل." : $"تم تطبيق Manual Scene على {count} جهاز.";
    }

    private Task SaveSceneAsync()
    {
        var scene = new D7RgbScene
        {
            Name = _sceneName.Text,
            Devices = _editors.Select(e => new D7RgbDeviceScene
            {
                DeviceIndex = e.Device.Index,
                DeviceName = e.Device.Name,
                Mode = e.Mode.SelectedItem?.ToString() ?? "static",
                Color = e.Hex.Text,
                Brightness = (int)Math.Round(e.Brightness.Value),
                Enabled = e.Enabled.IsChecked == true
            }).ToList()
        };
        _scenes.Save(scene);
        RefreshScenes();
        _sceneList.SelectedItem = scene.Name;
        _status.Text = $"تم حفظ Scene «{scene.Name}» لكل الأجهزة.";
        return Task.CompletedTask;
    }

    private async Task LoadSceneAsync()
    {
        var name = _sceneList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name)) { _status.Text = "اختر Scene."; return; }
        var scene = _scenes.Load(name);
        if (scene == null) { _status.Text = "Scene غير قابلة للقراءة."; return; }
        var applied = 0;
        foreach (var d in scene.Devices)
        {
            var e = _editors.FirstOrDefault(x => x.Device.Index == d.DeviceIndex || x.Device.Name.Equals(d.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (e == null) continue;
            e.Enabled.IsChecked = d.Enabled;
            e.Hex.Text = d.Color;
            e.Brightness.Value = d.Brightness;
            var mode = e.Mode.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), d.Mode, StringComparison.OrdinalIgnoreCase));
            if (mode != null) e.Mode.SelectedItem = mode;
            if (!d.Enabled) continue;
            await ApplyEditorAsync(e);
            applied++;
        }
        _sceneName.Text = scene.Name;
        _status.Text = $"Scene «{scene.Name}» طُبقت على {applied} جهاز متوافق.";
    }

    private Task DeleteSceneAsync()
    {
        var name = _sceneList.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(name) && _scenes.Delete(name)) _status.Text = $"تم حذف Scene «{name}».";
        RefreshScenes();
        return Task.CompletedTask;
    }

    private void RefreshScenes()
    {
        var selected = _sceneList.SelectedItem?.ToString();
        _sceneList.Items.Clear();
        foreach (var scene in _scenes.List()) _sceneList.Items.Add(scene);
        if (!string.IsNullOrWhiteSpace(selected) && _sceneList.Items.Contains(selected)) _sceneList.SelectedItem = selected;
        else if (_sceneList.Items.Count > 0) _sceneList.SelectedIndex = 0;
    }

    private void ConfigureIntelligence()
    {
        _temperature.Stop();
        _intelligenceTimer.Stop();
        _lastIntelligenceSignature = null;
        var mode = _intelligenceMode.SelectedItem?.ToString() ?? "Off / Manual";
        if (mode == "Temperature Guard")
        {
            _temperature.Start();
            _intelligenceState.Text = "Temperature Guard ON • أعلى حرارة CPU/GPU تقود اللون.";
            return;
        }
        if (mode == "Off / Manual")
        {
            _intelligenceState.Text = "Manual: كل جهاز يعمل بإعداد صفه.";
            return;
        }
        _intelligenceTimer.Start();
        _intelligenceState.Text = mode + " ON • مرتبط بمحرك D7KT الحي.";
        _ = IntelligenceTickAsync();
    }

    private async Task IntelligenceTickAsync()
    {
        if (_intelligenceBusy) return;
        _intelligenceBusy = true;
        try
        {
            var mode = _intelligenceMode.SelectedItem?.ToString() ?? "Off / Manual";
            var hardware = _hardware.Read();
            var game = _gameProvider();
            var mission = _missionProvider();
            string color;
            string description;
            switch (mode)
            {
                case "Performance Load":
                    var load = Math.Max(hardware.CpuLoad, hardware.GpuLoad);
                    color = load switch { < 30 => "00BFFF", < 55 => "00FF88", < 75 => "FFE000", < 90 => "FF7A00", _ => "FF1635" };
                    description = $"CPU {hardware.CpuLoad:0}% • GPU {hardware.GpuLoad:0}% • #{color}";
                    break;
                case "Game Presence":
                    color = string.IsNullOrWhiteSpace(game) ? "202020" : "D70F25";
                    description = string.IsNullOrWhiteSpace(game) ? "Desktop • Dim" : $"Gaming • {game} • D7KT red";
                    break;
                case "Mission Sync":
                    color = mission switch
                    {
                        D7Mission.ProRanked => "E00020",
                        D7Mission.StreamRanked => "A000FF",
                        D7Mission.Recording => "FF6500",
                        D7Mission.Story => "006CFF",
                        D7Mission.Silent => "FFD7A0",
                        _ => "303030"
                    };
                    description = $"{D7MissionEngine.MissionArabic(mission)} • #{color}";
                    break;
                default:
                    return;
            }

            var signature = mode + "|" + color + "|" + (game ?? "") + "|" + mission;
            _intelligenceState.Text = mode + " • " + description;
            if (string.Equals(signature, _lastIntelligenceSignature, StringComparison.Ordinal)) return;
            _lastIntelligenceSignature = signature;
            _status.Text = await _rgb.SetColorAsync(color);
        }
        catch (Exception ex) { _intelligenceState.Text = "RGB Intelligence: " + ex.Message; }
        finally { _intelligenceBusy = false; }
    }

    private static string DefaultColor(OpenRgbDevice device)
    {
        var text = (device.Name + " " + device.Type).ToLowerInvariant();
        if (text.Contains("mouse")) return "FFFFFF";
        if (text.Contains("ram") || text.Contains("memory")) return "7A20FF";
        if (text.Contains("fan") || text.Contains("cooler")) return "FF3300";
        return "D70F25";
    }

    private static void UpdatePreview(Border preview, string hex)
    {
        try
        {
            var raw = hex.Trim().TrimStart('#');
            if (raw.Length != 6) return;
            preview.Background = new SolidColorBrush(Color.FromRgb(
                Convert.ToByte(raw[..2], 16), Convert.ToByte(raw[2..4], 16), Convert.ToByte(raw[4..6], 16)));
        }
        catch { }
    }

    private Border Card() => new()
    {
        Background = Brush("Panel", Brushes.DimGray), BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = new Thickness(15), Margin = new Thickness(0, 7, 0, 7)
    };

    private Border Empty(string text) => new()
    {
        Background = Brush("Panel2", Brushes.DimGray), CornerRadius = new CornerRadius(10), Padding = new Thickness(14),
        Child = new TextBlock { Text = text, Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap }
    };

    private Button ActionButton(string text, Func<Task> action, bool accent = false)
    {
        var button = new Button
        {
            Content = text,
            Background = accent ? Brush("AccentStrong", Brushes.DarkRed) : Brush("Panel2", Brushes.DimGray),
            BorderBrush = accent ? Brush("Accent", Brushes.Red) : Brush("Border", Brushes.Gray)
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
