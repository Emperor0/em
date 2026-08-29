using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class Modern2026Bootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var window = (MainWindow)sender;
                window.Dispatcher.BeginInvoke(
                    new Action(window.ApplyModern2026Shell),
                    DispatcherPriority.ContextIdle);
            }),
            true);
    }
}

public partial class MainWindow
{
    private bool _modern2026Applied;

    internal void ApplyModern2026Shell()
    {
        if (_modern2026Applied) return;
        _modern2026Applied = true;

        Title = "D7 NEXUS • System Intelligence";
        Width = Math.Max(Width, 1420);
        Height = Math.Max(Height, 860);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>().Any());
        if (sidebar != null)
        {
            ModernizeBrand(sidebar);
            ModernizeNavigation(sidebar);
            WrapLiveStatus(sidebar);

            if (sidebar.Parent is ScrollViewer scroller && scroller.Parent is Border sidebarBorder)
            {
                sidebarBorder.CornerRadius = new CornerRadius(22);
                sidebarBorder.BorderThickness = new Thickness(1);
                sidebarBorder.BorderBrush = (Brush)FindResource("Border");
                sidebarBorder.Background = new LinearGradientBrush(
                    Color.FromRgb(16, 21, 31),
                    Color.FromRgb(12, 17, 26),
                    90);
                sidebarBorder.Padding = new Thickness(12);
            }
        }

        // Upgrade legacy cards without introducing blur or GPU-heavy effects.
        foreach (var border in FindVisualChildren<Border>(this))
        {
            if (border == sidebar?.Parent) continue;
            if (border.CornerRadius.TopLeft is > 0 and < 16)
                border.CornerRadius = new CornerRadius(16);

            if (border.Background is SolidColorBrush && border.BorderThickness == new Thickness(0))
            {
                border.BorderBrush = (Brush)FindResource("Border");
                border.BorderThickness = new Thickness(1);
            }
        }

        // Dashboard typography: modern hierarchy, still Arabic-first.
        foreach (var text in FindVisualChildren<TextBlock>(this))
        {
            if (text.Text == "مركز قيادة D7")
            {
                text.Text = "D7 NEXUS";
                text.FontSize = 34;
                text.FontWeight = FontWeights.Bold;
                text.Foreground = new LinearGradientBrush(
                    Color.FromRgb(247, 249, 252),
                    Color.FromRgb(124, 140, 255),
                    0);
            }
            else if (text.Text.StartsWith("مدير ذكي يراقب الجهاز", StringComparison.Ordinal))
            {
                text.Text = "Mission Control • أداء، بث، تسجيل، شبكة، أجهزة وتعريفات — من محرك واحد.";
                text.FontSize = 14;
            }
        }
    }

    private void ModernizeBrand(StackPanel sidebar)
    {
        var textBlocks = sidebar.Children.OfType<TextBlock>().ToArray();
        if (textBlocks.Length == 0) return;

        textBlocks[0].Text = "D7 NEXUS";
        textBlocks[0].FontSize = 29;
        textBlocks[0].FontWeight = FontWeights.Bold;
        textBlocks[0].Foreground = new LinearGradientBrush(
            Color.FromRgb(247, 249, 252),
            Color.FromRgb(124, 140, 255),
            0);

        if (textBlocks.Length > 1)
        {
            textBlocks[1].Text = "SYSTEM INTELLIGENCE • 2026";
            textBlocks[1].FontSize = 10.5;
            textBlocks[1].FontWeight = FontWeights.SemiBold;
            textBlocks[1].Foreground = (Brush)FindResource("Muted");
            textBlocks[1].Margin = new Thickness(0, 2, 0, 18);
        }
    }

    private void ModernizeNavigation(StackPanel sidebar)
    {
        var glyphs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["الرئيسية"] = "\uE80F",
            ["الألعاب والمنصات"] = "\uE7FC",
            ["التشخيص الذكي"] = "\uE9D9",
            ["الشبكة واللاتنسي"] = "\uE968",
            ["الأجهزة الطرفية"] = "\uE962",
            ["التعريفات"] = "\uE895",
            ["إعدادات Call of Duty"] = "\uE7FC",
            ["الحرارة والمراوح"] = "\uE9CA",
            ["مختبر الإدخال"] = "\uE961",
            ["الشاشة والتحكم"] = "\uE7F4",
            ["RGB Studio"] = "\uE790",
            ["Audio Studio"] = "\uE767",
            ["التخزين والأقراص"] = "\uEDA2",
            ["برامج بدء التشغيل"] = "\uE768",
            ["تطبيقات الخلفية"] = "\uECAA",
            ["الحذف الذكي من الجذور"] = "\uE74D",
            ["تشغيل D7 HUD"] = "\uE9D2",
            ["إيقاف D7 HUD"] = "\uE9D2",
            ["D7 Shadow Capture"] = "\uE714",
            ["Stream Director"] = "\uE93E",
            ["التحديثات والإصلاح"] = "\uE895"
        };

        foreach (var button in sidebar.Children.OfType<Button>().ToArray())
        {
            var label = button.Content?.ToString();
            if (string.IsNullOrWhiteSpace(label)) continue;

            button.Margin = new Thickness(0, 3, 0, 3);
            button.Padding = new Thickness(13, 10, 13, 10);
            button.MinHeight = 43;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.BorderThickness = new Thickness(1);

            var row = new Grid { FlowDirection = FlowDirection.RightToLeft };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new TextBlock
            {
                Text = glyphs.TryGetValue(label, out var glyph) ? glyph : "\uE10C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = (Brush)FindResource("Accent"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var title = new TextBlock
            {
                Text = label,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Text")
            };
            Grid.SetColumn(title, 1);
            row.Children.Add(icon);
            row.Children.Add(title);
            button.Content = row;
        }
    }

    private void WrapLiveStatus(StackPanel sidebar)
    {
        if (StatusText.Parent != sidebar) return;
        var index = sidebar.Children.IndexOf(StatusText);
        if (index < 0) return;

        sidebar.Children.RemoveAt(index);
        StatusText.Margin = new Thickness(0);
        StatusText.FontSize = 11.5;
        StatusText.Foreground = (Brush)FindResource("Muted");

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("Success"),
            Margin = new Thickness(0, 2, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(StatusText, 1);
        grid.Children.Add(dot);
        grid.Children.Add(StatusText);

        var chip = new Border
        {
            Background = (Brush)FindResource("AccentSoft"),
            BorderBrush = (Brush)FindResource("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 14, 0, 2),
            Child = grid
        };
        sidebar.Children.Insert(index, chip);
    }
}
