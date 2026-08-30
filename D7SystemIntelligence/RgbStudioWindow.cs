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
    private readonly Func<string?>? _gameProvider;
    private readonly Func<D7Mission>? _missionProvider;
    private readonly ManagedOpenRgbService _rgb = new();
    private readonly TemperatureRgbController _temperature;
    private readonly RgbSceneStore _scenes = new();
    private readonly TextBlock _status = new();
    private readonly StackPanel _deviceEditors = new();
    private readonly List<DeviceEditor> _editors = [];
    private readonly ComboBox _sceneList = new() { MinWidth = 190 };
    private readonly TextBox _sceneName = new() { MinWidth = 190, Text = "D7KT Scene" };
    private readonly ComboBox _intelligenceMode = new() { MinWidth = 210 };
    private readonly TextBlock _intelligenceState = new();
    private readonly DispatcherTimer _intelligenceTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? _lastIntelligenceColor;
    private bool _intelligenceBusy;

    public RgbStudioWindow(HardwareEngine hardware, Func<string?>? gameProvider = null, Func<D7Mission>? missionProvider = null)
    {
        _hardware = hardware;
        _gameProvider = gameProvider;
        _missionProvider = missionProvider;
        _temperature = new TemperatureRgbController(hardware, _rgb);
        _temperature.StatusChanged += message => Dispatcher.Invoke(() => _status.Text = message);

        Title = "D7KT — RGB Intelligence Studio";
        Width = 1120;
        Height = 800;
        MinWidth = 920;
        MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        Content = BuildUi();
        _intelligenceTimer.Tick += async (_, _) => await RunIntelligenceTickAsync();
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

        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "RGB Intelligence Studio", FontSize = 30, FontWeight = FontWeights.Bold });
        title.Children.Add(new TextBlock
        {
            Text = "مو مجرد ألوان جاهزة: تحكم مستقل بكل جهاز، Mode/Brightness/Color، Scenes محفوظة، وربط الإضاءة بحرارة الجهاز والحمل واللعبة والـMission.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Muted", Brushes.Gray),
            Margin = new Thickness(0, 6, 0, 12)
        });
        root.Children.Add(title);

        var backend = Card();
        var backendGrid = new Grid();
        backendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        backendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var backendText = new StackPanel();
        backendText.Children.Add(new TextBlock { Text = "OpenRGB Hardware Backend", FontSize = 17, FontWeight = FontWeights.SemiBold });
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("Muted", Brushes.Gray);
        _status.Margin = new Thickness(0, 6, 0, 0);
        backendText.Children.Add(_status);
        Grid.SetColumn(backendText, 0);
        backendGrid.Children.Add(backendText);

        var backendButtons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        backendButtons.Children.Add(Button("تجهيز / تحديث Backend", PrepareBackendAsync));
        backendButtons.Children.Add(Button("فتح Advanced Studio", () =>
        {
            _status.Text = _rgb.LaunchAdvancedStudio();
            return Task.CompletedTask;
        }, true));
        Grid.SetColumn(backendButtons, 1);
        backendGrid.Children.Add(backendButtons);
        backend.Child = backendGrid;
        root.Children.Add(backend);

        var devicesCard = Card();
        var devicesStack = new StackPanel();
        var devicesHeader = new Grid();
        devicesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        devicesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        devicesHeader.Children.Add(new TextBlock { Text = "Device Matrix", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var refresh = Button("إعادة فحص الأجهزة", RefreshDevicesAsync);
        Grid.SetColumn(refresh, 1);
        devicesHeader.Children.Add(refresh);
        devicesStack.Children.Add(devicesHeader);
        devicesStack.Children.Add(new TextBlock
        {
            Text = "كل صف جهاز مستقل: تقدر تخلي الكيبورد أحمر، الماوس أبيض، الرام بنفسجي، والمراوح Rainbow — حسب ما يدعمه الجهاز نفسه.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Muted", Brushes.Gray),
            Margin = new Thickness(0, 6, 0, 10)
        });
        _deviceEditors.Children.Add(Empty("جاري اكتشاف أجهزة RGB…"));
        devicesStack.Children.Add(_deviceEditors);
        devicesCard.Child = devicesStack;
        root.Children.Add(devicesCard);

        var sceneCard = Card();
        var sceneStack = new StackPanel();
        sceneStack.Children.Add(new TextBlock { Text = "D7KT Scenes", FontSize = 20, FontWeight = FontWeights.SemiBold });
        sceneStack.Children.Add(new TextBlock
        {
            Text = "Scene تحفظ إعداد كل جهاز بشكل مستقل. مثال: Red Ranked / White Desktop / Horror / Stream.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 9)
        });
        var sceneRow = new WrapPanel();
        sceneRow.Children.Add(_sceneName);
        sceneRow.Children.Add(Button("حفظ Scene الحالية", SaveSceneAsync, true));
        sceneRow.Children.Add(_sceneList);
        sceneRow.Children.Add(Button("تحميل وتطبيق", LoadSceneAsync));
        sceneRow.Children.Add(Button("حذف", DeleteSceneAsync));
        sceneStack.Children.Add(sceneRow);
        sceneCard.Child = sceneStack;
        root.Children.Add(sceneCard);

        var intelligenceCard = Card();
        var intelligenceStack = new StackPanel();
        intelligenceStack.Children.Add(new TextBlock { Text = "RGB Intelligence", FontSize = 20, FontWeight = FontWeights.SemiBold });
        intelligenceStack.Children.Add(new TextBlock
        {
            Text = "هذه نقطة D7KT المختلفة: الإضاءة تتفاعل مع حالة الجهاز بدل مؤثر شكلي فقط. الأوامر ترسل فقط عند تغير الحالة لتجنب حمل غير ضروري.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 9)
        });
        foreach (var item in new[]
                 {
                     "Off / Manual",
                     "Temperature Guard",
                     "Performance Load",
                     "Game Presence",
                     "Mission Sync"
                 })
            _intelligenceMode.Items.Add(item);
        _intelligenceMode.SelectedIndex = 0;
        _intelligenceMode.SelectionChanged += (_, _) => ConfigureIntelligenceMode();
        var intelligenceRow = new WrapPanel();
        intelligenceRow.Children.Add(_intelligenceMode);
        intelligenceRow.Children.Add(Button("تطبيق Manual على الكل", ApplyAllManualAsync, true));
        intelligenceRow.Children.Add(Button("إطفاء الكل", async () => _status.Text = await _rgb.TurnOffAsync()));
        intelligenceStack.Children.Add(intelligenceRow);
        _intelligenceState.Foreground = Brush("Muted", Brushes.Gray);
        _intelligenceState.TextWrapping = TextWrapping.Wrap;
        _intelligenceState.Margin = new Thickness(0, 8, 0, 0);
        _intelligenceState.Text = "Manual: كل جهاز يعمل بإعداد صفه.";
        intelligenceStack.Children.Add(_intelligenceState);
        intelligenceCard.Child = intelligenceStack;
        root.Children.Add(intelligenceCard);

        var capabilities = Card();
        var capStack = new StackPanel();
        capStack.Children.Add(new TextBlock { Text = "Advanced Effects / Zones / Per‑LED", FontSize = 20, FontWeight = FontWeights.SemiBold });
        capStack.Children.Add(new TextBlock
        {
            Text = "للأجهزة التي تدعم Direct Mode أو Zones/Per‑LED، افتح Advanced Studio. D7KT يتعمد عدم ادعاء دعم Per‑LED لجهاز لا يعلنه OpenRGB. هناك تقدر تستخدم Visual Map وEffects Plugins وAmbilight/Audio/Shader حسب الـplugins المثبتة، بينما D7KT يدير الـAutomation والScenes الذكية.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8)
        });
        capStack.Children.Add(Button("فتح OpenRGB Advanced Studio", () =>
        {
            _status.Text = _rgb.LaunchAdvancedStudio();
            return Task.CompletedTask;
        }, true));
        capabilities.Child = capStack;
        root.Children.Add(capabilities);

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private async Task InitializeAsync()
    {
        var detected = _rgb.Detect();
        _status.Text = detected.Detail;
        RefreshSceneList();
        if (detected.Available)
            await RefreshDevicesAsync();
        else
            _deviceEditors.Children.Clear();
    }

    private async Task PrepareBackendAsync()
    {
        try
        {
            var progress = new Progress<double>(p => _status.Text = $"جاري تجهيز OpenRGB الرسمي… {p:0}%");
            var info = await _rgb.EnsureAsync(progress);
            _status.Text = info.Detail;
            await RefreshDevicesAsync();
        }
        catch (Exception ex) { _status.Text = "OpenRGB: " + ex.Message; }
    }

    private async Task RefreshDevicesAsync()
    {
        _deviceEditors.Children.Clear();
        _deviceEditors.Children.Add(Empty("جاري قراءة الأجهزة والمودات المدعومة…"));
        _editors.Clear();
        try
        {
            var devices = await _rgb.GetDevicesAsync();
            _deviceEditors.Children.Clear();
            if (devices.Count == 0)
            {
                _deviceEditors.Children.Add(Empty("OpenRGB لم يكتشف أجهزة RGB مدعومة. هذا ليس معناه أن الجهاز بلا إضاءة؛ قد يحتاج دعم/صلاحية/تعريف خاص بالهاردوير."));
                return;
            }

            foreach (var device in devices)
            {
                var editor = CreateDeviceEditor(device);
                _editors.Add(editor);
                _deviceEditors.Children.Add(DeviceRow(editor));
            }
            _status.Text = $"تم اكتشاف {devices.Count} جهاز RGB. التحكم الآن مستقل لكل جهاز.";
        }
        catch (Exception ex)
        {
            _deviceEditors.Children.Clear();
            _deviceEditors.Children.Add(Empty("فشل فحص RGB: " + ex.Message));
        }
    }

    private DeviceEditor CreateDeviceEditor(OpenRgbDevice device)
    {
        var mode = new ComboBox { MinWidth = 150 };
        var modes = device.Modes.Count > 0 ? device.Modes : ["static"];
        foreach (var item in modes.Distinct(StringComparer.OrdinalIgnoreCase)) mode.Items.Add(item);
        if (!modes.Any(x => x.Equals("static", StringComparison.OrdinalIgnoreCase))) mode.Items.Insert(0, "static");
        mode.SelectedIndex = 0;

        var hex = new TextBox { Text = DefaultColorFor(device), Width = 96, FlowDirection = FlowDirection.LeftToRight };
        var brightness = new Slider { Minimum = 0, Maximum = 100, Value = 100, Width = 120, TickFrequency = 5, IsSnapToTickEnabled = false };
        var preview = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), BorderBrush = Brush("Border", Brushes.Gray), BorderThickness = new Thickness(1) };
        UpdatePreview(preview, hex.Text);
        hex.TextChanged += (_, _) => UpdatePreview(preview, hex.Text);

        return new DeviceEditor
        {
            Device = device,
            Mode = mode,
            Hex = hex,
            Brightness = brightness,
            Preview = preview,
            Enabled = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    private UIElement DeviceRow(DeviceEditor editor)
    {
        var border = new Border
        {
            Background = Brush("Panel2", Brushes.DimGray),
            BorderBrush = Brush("Border", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 4)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(editor.Enabled, 0);
        grid.Children.Add(editor.Enabled);

        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = $"#{editor.Device.Index}  {editor.Device.Name}", FontSize = 15, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight });
        info.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(editor.Device.Type) ? "OpenRGB device" : editor.Device.Type,
            FontSize = 10.5, Foreground = Brush("Muted", Brushes.Gray), FlowDirection = FlowDirection.LeftToRight
        });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var controls = new WrapPanel { FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(new TextBlock { Text = "Mode", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Muted", Brushes.Gray) });
        controls.Children.Add(editor.Mode);
        controls.Children.Add(editor.Preview);
        controls.Children.Add(editor.Hex);
        controls.Children.Add(new TextBlock { Text = "Brightness", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(8, 0, 2, 0) });
        controls.Children.Add(editor.Brightness);
        controls.Children.Add(Button("تطبيق", async () => await ApplyEditorAsync(editor), true));
        controls.Children.Add(Button("إطفاء", async () => _status.Text = await _rgb.TurnOffDeviceAsync(editor.Device.Index)));
        Grid.SetColumn(controls, 2);
        grid.Children.Add(controls);
        border.Child = grid;
        return border;
    }

    private async Task ApplyEditorAsync(DeviceEditor editor)
    {
        if (editor.Enabled.IsChecked != true) return;
        _status.Text = await _rgb.SetDeviceModeAsync(
            editor.Device.Index,
            editor.Mode.SelectedItem?.ToString() ?? "static",
            editor.Hex.Text,
            (int)Math.Round(editor.Brightness.Value));
    }

    private async Task ApplyAllManualAsync()
    {
        var results = new List<string>();
        foreach (var editor in _editors.Where(x => x.Enabled.IsChecked == true))
        {
            results.Add(await _rgb.SetDeviceModeAsync(
                editor.Device.Index,
                editor.Mode.SelectedItem?.ToString() ?? "static",
                editor.Hex.Text,
                (int)Math.Round(editor.Brightness.Value)));
        }
        _status.Text = results.Count == 0 ? "لا يوجد جهاز مفعّل للتطبيق." : $"تم تطبيق Manual Scene على {results.Count} جهاز.";
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
        RefreshSceneList();
        _sceneList.SelectedItem = scene.Name;
        _status.Text = $"تم حفظ Scene «{scene.Name}» بكل إعدادات الأجهزة.";
        return Task.CompletedTask;
    }

    private async Task LoadSceneAsync()
    {
        var name = _sceneList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name)) { _status.Text = "اختر Scene أولًا."; return; }
        var scene = _scenes.Load(name);
        if (scene == null) { _status.Text = "Scene غير موجودة أو تالفة."; return; }

        var applied = 0;
        foreach (var item in scene.Devices)
        {
            var editor = _editors.FirstOrDefault(e =>
                e.Device.Index == item.DeviceIndex || e.Device.Name.Equals(item.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (editor == null) continue;
            editor.Enabled.IsChecked = item.Enabled;
            editor.Hex.Text = item.Color;
            editor.Brightness.Value = item.Brightness;
            var mode = editor.Mode.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), item.Mode, StringComparison.OrdinalIgnoreCase));
            if (mode != null) editor.Mode.SelectedItem = mode;
            if (!item.Enabled) continue;
            await ApplyEditorAsync(editor);
            applied++;
        }
        _sceneName.Text = scene.Name;
        _status.Text = $"تم تحميل Scene «{scene.Name}» وتطبيقها على {applied} جهاز متوافق.";
    }

    private Task DeleteSceneAsync()
    {
        var name = _sceneList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        if (_scenes.Delete(name)) _status.Text = $"تم حذف Scene «{name}».";
        RefreshSceneList();
        return Task.CompletedTask;
    }

    private void RefreshSceneList()
    {
        var selected = _sceneList.SelectedItem?.ToString();
        _sceneList.Items.Clear();
        foreach (var name in _scenes.List()) _sceneList.Items.Add(name);
        if (!string.IsNullOrWhiteSpace(selected) && _sceneList.Items.Contains(selected)) _sceneList.SelectedItem = selected;
        else if (_sceneList.Items.Count > 0) _sceneList.SelectedIndex = 0;
    }

    private void ConfigureIntelligenceMode()
    {
        _temperature.Stop();
        _intelligenceTimer.Stop();
        _lastIntelligenceColor = null;
        var mode = _intelligenceMode.SelectedItem?.ToString() ?? "Off / Manual";
        if (mode == "Temperature Guard")
        {
            _temperature.Start();
            _intelligenceState.Text = "Temperature Guard ON • اللون يمثل أعلى حرارة CPU/GPU.";
            return;
        }
        if (mode == "Off / Manual")
        {
            _intelligenceState.Text = "Manual: كل جهاز يعمل بإعداد صفه.";
            return;
        }
        _intelligenceTimer.Start();
        _intelligenceState.Text = mode + " ON • D7KT يرسل تغييرًا فقط عندما تتغير الحالة.";
        _ = RunIntelligenceTickAsync();
    }

    private async Task RunIntelligenceTickAsync()
    {
        if (_intelligenceBusy) return;
        _intelligenceBusy = true;
        try
        {
            var mode = _intelligenceMode.SelectedItem?.ToString() ?? "Off / Manual";
            var h = _hardware.Read();
            var game = _gameProvider?.Invoke();
            var mission = _missionProvider?.Invoke() ?? D7Mission.None;
            string color;
            string description;

            switch (mode)
            {
                case "Performance Load":
                {
                    var load = Math.Max(h.CpuLoad, h.GpuLoad);
                    color = load switch
                    {
                        < 30 => "00BFFF",
                        < 55 => "00FF88",
                        < 75 => "FFE000",
                        < 90 => "FF7A00",
                        _ => "FF1635"
                    };
                    description = $"Performance Load • CPU {h.CpuLoad:0}% • GPU {h.GpuLoad:0}% • #{color}";
                    break;
                }
                case "Game Presence":
                    color = string.IsNullOrWhiteSpace(game) ? "242424" : "D70F25";
                    description = string.IsNullOrWhiteSpace(game) ? "Desktop • RGB dim" : $"Gaming • {game} • D7KT red";
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
                    description = $"Mission Sync • {D7MissionEngine.MissionArabic(mission)} • #{color}";
                    break;
                default:
                    return;
            }

            _intelligenceState.Text = description;
            if (string.Equals(color, _lastIntelligenceColor, StringComparison.OrdinalIgnoreCase)) return;
            _lastIntelligenceColor = color;
            _status.Text = await _rgb.SetColorAsync(color);
        }
        catch (Exception ex) { _intelligenceState.Text = "RGB Intelligence: " + ex.Message; }
        finally { _intelligenceBusy = false; }
    }

    private static string DefaultColorFor(OpenRgbDevice device)
    {
        var text = (device.Name + " " + device.Type).ToLowerInvariant();
        if (text.Contains("keyboard")) return "D70F25";
        if (text.Contains("mouse")) return "FFFFFF";
        if (text.Contains("ram") || text.Contains("memory")) return "7A20FF";
        if (text.Contains("gpu")) return "E00020";
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
                Convert.ToByte(raw[0..2], 16),
                Convert.ToByte(raw[2..4], 16),
                Convert.ToByte(raw[4..6], 16)));
        }
        catch { }
    }

    private Border Card() => new()
    {
        Background = Brush("Panel", Brushes.DimGray),
        BorderBrush = Brush("Border", Brushes.Gray),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(15),
        Margin = new Thickness(0, 8, 0, 8)
    };

    private Border Empty(string text) => new()
    {
        Background = Brush("Panel2", Brushes.DimGray),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(15),
        Child = new TextBlock { Text = text, Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap }
    };

    private Button Button(string text, Func<Task> action, bool accent = false)
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
