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
        Title = "D7 NEXUS • Mission Control";
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
            Foreground = new LinearGradientBrush(Color.FromRgb(247, 249, 252), Color.FromRgb(124, 140, 255), 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "اختيار واحد ينسق الطاقة، الشاشة، الشبكة، المراوح، البث والتسجيل مع Restore حقيقي.",
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
        missions.Children.Add(MissionCard("PRO RANKED", "أقصى استجابة للرانك: High Performance + أعلى Hz + Gaming Network + تنظيف خلفية آمن + Smart Fans.", D7Mission.ProRanked));
        missions.Children.Add(MissionCard("STREAM + RANKED", "اللعب والبث معًا: Priorities محسوبة + Network + أعلى Hz + Fans، ويستخدم Replay الموجود بدون مسجل ثانٍ.", D7Mission.StreamRanked));
        missions.Children.Add(MissionCard("RECORDING", "تسجيل المقاطع: Shadow Capture الحقيقي + High Performance + Smart Fans، مع الحفاظ على Replay Buffer واحد.", D7Mission.Recording));
        missions.Children.Add(MissionCard("STORY / ULTRA", "للألعاب القصصية: High Performance + أعلى Refresh مدعوم + تبريد تلقائي إذا الهاردوير يسمح.", D7Mission.Story));
        missions.Children.Add(MissionCard("SILENT", "للخمول والعمل الهادئ: Balanced Power وإرجاع أي Fan override إلى BIOS/AUTO.", D7Mission.Silent));

        var restore = new Button
        {
            Content = "إيقاف المهمة + استعادة كل شيء",
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
        _log.Text = "D7 ينتظر اختيار Mission.\r\n";
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
                Append($"{(step.Success ? "✓" : "!")} {step.Step}\r\n{step.Detail}");
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
                Append($"{(step.Success ? "✓" : "!")} {step.Step}\r\n{step.Detail}");
            Append(result.Summary);
        }
        catch (Exception ex) { Append("خطأ الاستعادة: " + ex.Message); }
        finally { SetBusy(false); RefreshHeader(); }
    }

    private void RefreshHeader()
    {
        var game = _gameProvider();
        _status.Text = $"Mission: {D7MissionEngine.MissionArabic(_engine.ActiveMission)}   •   Game: {(string.IsNullOrWhiteSpace(game) ? "لا توجد لعبة مكتشفة" : game)}   •   Restore Vault: مفعّل";
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
        _status.Text = busy ? "D7 ينفذ المهمة الآن… لا تغلق النافذة حتى ينتهي Restore/Apply." : _status.Text;
    }
}
