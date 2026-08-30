using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal sealed class AppIntelligenceIntegration : IDisposable
{
    private readonly Window _owner;
    private Button? _devicesButton;
    private bool _disposed;

    private AppIntelligenceIntegration(Window owner)
    {
        _owner = owner;
        owner.Loaded += OnLoaded;
        owner.Closed += (_, _) => Dispose();
    }

    public static AppIntelligenceIntegration Attach(Window owner) => new(owner);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _devicesButton = FindDescendants<Button>(_owner).FirstOrDefault(x => Equals(x.Tag, "devices"));
        if (_devicesButton != null) _devicesButton.Click += OnDevicesClicked;
    }

    private void OnDevicesClicked(object sender, RoutedEventArgs e)
        => _owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, InjectDevicesCard);

    private void InjectDevicesCard()
    {
        if (_disposed) return;
        if (FindDescendants<FrameworkElement>(_owner).Any(x => Equals(x.Tag, "app-intelligence-card"))) return;

        var targetGrid = FindDescendants<UniformGrid>(_owner)
            .FirstOrDefault(g => g.Columns == 4 && ContainsText(g, "Driver Safety") && ContainsText(g, "Storage Center"));
        if (targetGrid == null) return;

        var card = new Border
        {
            Tag = "app-intelligence-card",
            Background = Brush("Panel"),
            BorderBrush = Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(15),
            Margin = new Thickness(4)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "البرامج الذكية", FontSize = 15, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = "Discord • Steam • NVIDIA App • OBS • TikTok • Browsers\nProfiles + Startup + Cache + Restore",
            Foreground = Brush("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 8),
            MinHeight = 34
        });
        var open = new Button { Content = "فتح", HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += (_, _) =>
        {
            var window = new AppIntelligenceWindow { Owner = _owner, Icon = _owner.Icon };
            window.ShowDialog();
        };
        stack.Children.Add(open);
        card.Child = stack;
        targetGrid.Children.Insert(0, card);
    }

    private static bool ContainsText(DependencyObject root, string text)
        => FindDescendants<TextBlock>(root).Any(x => x.Text.Equals(text, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Loaded -= OnLoaded;
        if (_devicesButton != null) _devicesButton.Click -= OnDevicesClicked;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var item in FindDescendants<T>(child)) yield return item;
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
