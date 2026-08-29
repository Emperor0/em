using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class AutoSceneWindow : Window
{
    private readonly AutoSceneDirector _director;
    private readonly Func<string> _statusProvider;
    private readonly CheckBox _enabled = new();
    private readonly TextBox _delay = new();
    private readonly TextBox _tokens = new();
    private readonly TextBlock _status = new();

    public AutoSceneWindow(AutoSceneDirector director, Func<string> statusProvider)
    {
        _director = director;
        _statusProvider = statusProvider;
        Title = "D7 NEXUS • Auto Scene";
        Width = 760;
        Height = 600;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
        LoadSettings();
        Loaded += (_, _) => RefreshStatus();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "AUTO SCENE ENGINE", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "D7 يراقب المشهد فقط. بعد ثبات اللعبة لعدة ثوانٍ: تنافسية → PRO RANKED، لعبة + بث → STREAM + RANKED، قصصية → STORY. عند إغلاق اللعبة يرجع كل الإعدادات.",
            Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var card = new Border
        {
            Background = (Brush)Application.Current.FindResource("Panel"), BorderBrush = (Brush)Application.Current.FindResource("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Margin = new Thickness(0, 18, 0, 14)
        };
        var settings = new StackPanel();
        _enabled.Content = "تفعيل Auto Scene";
        _enabled.FontSize = 16;
        settings.Children.Add(_enabled);
        settings.Children.Add(new TextBlock { Text = "مهلة ثبات المشهد قبل التطبيق (ثانية)", Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(4, 10, 4, 3) });
        _delay.Width = 120; _delay.HorizontalAlignment = HorizontalAlignment.Right; settings.Children.Add(_delay);
        settings.Children.Add(new TextBlock { Text = "كلمات تعريف الألعاب التنافسية — سطر أو فاصلة لكل كلمة", Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(4, 12, 4, 3) });
        _tokens.AcceptsReturn = true; _tokens.Height = 150; _tokens.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; settings.Children.Add(_tokens);
        card.Child = settings; Grid.SetRow(card, 1); root.Children.Add(card);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.FontSize = 14;
        _status.Padding = new Thickness(14);
        var statusCard = new Border { Background = (Brush)Application.Current.FindResource("AccentSoft"), BorderBrush = (Brush)Application.Current.FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = _status };
        Grid.SetRow(statusCard, 2); root.Children.Add(statusCard);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var refresh = new Button { Content = "تحديث الحالة", MinWidth = 120 };
        refresh.Click += (_, _) => RefreshStatus();
        var save = new Button { Content = "حفظ وتفعيل", MinWidth = 130, Background = (Brush)Application.Current.FindResource("AccentSoft") };
        save.Click += (_, _) => SaveSettings();
        actions.Children.Add(refresh); actions.Children.Add(save);
        Grid.SetRow(actions, 3); root.Children.Add(actions);
        return root;
    }

    private void LoadSettings()
    {
        var s = _director.Settings;
        _enabled.IsChecked = s.Enabled;
        _delay.Text = s.StabilityDelaySeconds.ToString();
        _tokens.Text = string.Join(Environment.NewLine, s.CompetitiveGameTokens);
    }

    private void SaveSettings()
    {
        if (!int.TryParse(_delay.Text.Trim(), out var delay)) delay = 8;
        var tokens = _tokens.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var settings = new AutoSceneSettings { Enabled = _enabled.IsChecked == true, StabilityDelaySeconds = delay, CompetitiveGameTokens = tokens };
        _director.Save(settings);
        LoadSettings();
        RefreshStatus();
        _status.Text += "\nتم حفظ إعدادات Auto Scene.";
    }

    private void RefreshStatus()
    {
        _status.Text = _statusProvider();
    }
}
