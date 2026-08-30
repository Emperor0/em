using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

internal sealed class NetworkLabIntegration : IDisposable
{
    private readonly Window _owner;
    private bool _disposed;

    private NetworkLabIntegration(Window owner)
    {
        _owner = owner;
        owner.Loaded += OnLoaded;
        owner.Closed += (_, _) => Dispose();
    }

    public static NetworkLabIntegration Attach(Window owner) => new(owner);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var legacy = FindDescendants<Button>(_owner)
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), "Gaming Network", StringComparison.Ordinal));
        if (legacy?.Parent is not WrapPanel panel) return;
        if (panel.Children.OfType<Button>().Any(x => Equals(x.Tag, "network-lab"))) return;

        panel.Children.Clear();
        var button = new Button
        {
            Tag = "network-lab",
            Content = "فتح Network Lab",
            MinWidth = 180,
            Background = Brush("AccentStrong"),
            BorderBrush = Brush("Accent")
        };
        button.Click += (_, _) =>
        {
            var window = new NetworkLabWindow { Owner = _owner, Icon = _owner.Icon };
            window.ShowDialog();
        };
        panel.Children.Add(button);

        var note = new TextBlock
        {
            Text = "Gaming NIC القديم تم دمجه هنا: Diagnose + Remote Host + Bufferbloat + Before/After + Auto Rollback.",
            Foreground = Brush("Muted"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        panel.Children.Add(note);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Loaded -= OnLoaded;
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
