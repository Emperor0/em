using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class MissionControlWindow : Window
{
    private readonly D7MissionEngine _engine;
    private readonly Func<string?> _gameProvider;
    private readonly TextBlock _status = new();
    private readonly TextBox _log = new();
    private readonly List<Button> _buttons = new();

    public MissionControlWindow(D7MissionEngine engine, Func<string?> gameProvider)
    {
        _engine = engine;
        _gameProvider = gameProvider;
        Title = "D7KT • Mission Control";
        Width = 980;
        Height = 720;
        MinWidth = 860;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Foreground = (Brush)Application.Current.FindResource("Text");
        Content = BuildUi();
        _engine.StatusChanged += OnEngineStatus;
        Closed += (_, _) => _engine.StatusChanged -= OnEngineStatus;
        RefreshHeader();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "MISSION CONTROL",
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("Accent")
        });
        header.Children.Add(new TextBlock
        {
            Text = "كل خطوة تصنف الآن: Applied / Verified / Already Optimal / Unsupported / Skipped / Failed. النجاح لا يعني أن D7KT غيّر شيئًا إذا لم يتغير شيء فعلًا.",
            Foreground = (Brush)Application.Current.FindResource("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var statusCard = new Border
        {
            Background = (Brush)Application.Current.FindResource("Panel"),
            BorderBrush = (Brush)Application.Current.FindResource("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 18, 0, 16),
            Child = _status
        };
        _status.FontSize = 15;
        _status.FontWeight = FontWeights.SemiBold;
        _status.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(statusCard, 1);
        root.Children.Add(statusCard);

        var missions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 16) };
        missions.Children.Add(MissionCard("PRO RANKED", "High Performance + أعلى Hz + Gaming Network + تنظيف خلفية آمن + Smart Fans فقط إن كانت writable.", D7Mission.ProRanked));
        missions.Children.Add(MissionCard("STREAM + RANKED", "Priority Governor + Network + Display + Replay الموجود، بدون duplicate recorder.", D7Mission.StreamRanked));
        missions.Children.Add(MissionCard("RECORDING", "Shadow Capture + Power + Display/Fans المدعومة مع تحقق بعد التطبيق.", D7Mission.Recording));
        missions.Children.Add(MissionCard("STORY / ULTRA", "High Performance + أعلى Refresh مدعوم + تبريد تلقائي إن كان الهاردوير يسمح.", D7Mission.Story));
        missions.Children.Add(MissionCard("SILENT", "Balanced Power وإرجاع Fan override إلى BIOS/AUTO.", D7Mission.Silent));

        var restore = new Button
        {
            Content = "إيقاف المهمة + استعادة تغييرات D7KT فقط",
            MinHeight = 104,
            Margin = new Thickness(6),
            Background = (Brush)Application.Current.FindResource("AccentSoft")
        };
        restore.Click += async (_, _) => await RunRestoreAsync();
        _buttons.Add(restore);
        missions.Children.Add(restore);
        Grid.SetRow(missions, 2);
        root.Children.Add(missions);

        _log.IsReadOnly = true;
        _log.AcceptsReturn = true;
        _log.TextWrapping = TextWrapping.Wrap;
        _log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _log.Background = (Brush)Application.Current.FindResource("Panel");
        _log.BorderBrush = (Brush)Application.Current.FindResource("Border");
        _log.BorderThickness = new Thickness(1);
        _log.Padding = new Thickness(14);
        _log.FontFamily = new FontFamily("Consolas");
        _log.FontSize = 12.5;
        _log.Text = "D7KT ينتظر اختيار Mission.\r\n";
        Grid.SetRow(_log, 3);
        root.Children.Add(_log);

        return root;
    }

    private Border MissionCard(string title, string description, D7Mission mission)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = (Brush)Application.Current.FindResource("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        var button = new Button { Content = "تشغيل", HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => await RunMissionAsync(mission);
        _buttons.Add(button);
        stack.Children.Add(button);

        return new Border
        {
            Background = (Brush)Application.Current.FindResource("Panel"),
            BorderBrush = (Brush)Application.Current.FindResource("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Margin = new Thickness(6),
            Child = stack
        };
    }

    private async Task RunMissionAsync(D7Mission mission)
    {
        SetBusy(true);
        try
        {
            Append($"\r\n▶ {D7MissionEngine.MissionArabic(mission)}");
            var result = await _engine.ApplyAsync(mission, _gameProvider());
            foreach (var step in result.Steps)
                Append($"{Icon(step.State)} [{step.State}] {step.Step}\r\n{step.Detail}");
            Append(result.Summary);
        }
        catch (Exception ex)
        {
            Append("خطأ Mission Control: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshHeader();
        }
    }

    private async Task RunRestoreAsync()
    {
        SetBusy(true);
        try
        {
            Append("\r\n↩ RESTORE");
            var result = await _engine.RestoreAsync();
            foreach (var step in result.Steps)
                Append($"{Icon(step.State)} [{step.State}] {step.Step}\r\n{step.Detail}");
            Append(result.Summary);
        }
        catch (Exception ex) { Append("خطأ الاستعادة: " + ex.Message); }
        finally { SetBusy(false); RefreshHeader(); }
    }

    private static string Icon(MissionStepState state) => state switch
    {
        MissionStepState.Applied or MissionStepState.Verified or MissionStepState.Restored => "✓",
        MissionStepState.AlreadyOptimal => "=",
        MissionStepState.Unsupported or MissionStepState.Skipped => "○",
        MissionStepState.Failed => "!",
        _ => "•"
    };

    private void RefreshHeader()
    {
        var game = _gameProvider();
        _status.Text = $"Mission: {D7MissionEngine.MissionArabic(_engine.ActiveMission)}   •   Game: {(string.IsNullOrWhiteSpace(game) ? "لا توجد لعبة مكتشفة" : game)}   •   Restore: تغييرات D7KT فقط";
    }

    private void OnEngineStatus(string text)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshHeader();
            Append(text);
        });
    }

    private void Append(string text)
    {
        _log.AppendText(text + Environment.NewLine);
        _log.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        foreach (var button in _buttons) button.IsEnabled = !busy;
        _status.Text = busy ? "D7KT ينفذ المهمة ويحقق من النتيجة…" : _status.Text;
    }
}
