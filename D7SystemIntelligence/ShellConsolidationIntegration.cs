using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal sealed class ShellConsolidationIntegration : IDisposable
{
    private readonly Window _owner;
    private Button? _devices;
    private Button? _updates;
    private bool _disposed;

    private ShellConsolidationIntegration(Window owner)
    {
        _owner = owner;
        owner.Loaded += OnLoaded;
        owner.Closed += (_, _) => Dispose();
    }

    public static ShellConsolidationIntegration Attach(Window owner) => new(owner);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _devices = FindDescendants<Button>(_owner).FirstOrDefault(x => Equals(x.Tag, "devices"));
        _updates = FindDescendants<Button>(_owner).FirstOrDefault(x => Equals(x.Tag, "updates"));
        if (_devices != null) _devices.Click += OnDevices;
        if (_updates != null) _updates.Click += OnUpdates;
    }

    private void OnDevices(object sender, RoutedEventArgs e)
        => _owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ConsolidateDevices);

    private void OnUpdates(object sender, RoutedEventArgs e)
        => _owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ConsolidateUpdates);

    private void ConsolidateDevices()
    {
        var grid = FindDescendants<UniformGrid>(_owner)
            .FirstOrDefault(g => g.Columns == 4 && ContainsText(g, "Driver Safety") && ContainsText(g, "Storage Center"));
        if (grid == null) return;

        var remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Startup Manager",
            "Background Apps",
            "Smart Removal",
            "Crash Investigator",
            "Restore Vault",
            "Full Health"
        };
        foreach (var child in grid.Children.Cast<UIElement>().ToArray())
        {
            if (remove.Any(title => ContainsText(child, title)))
                grid.Children.Remove(child);
        }
    }

    private void ConsolidateUpdates()
    {
        var grid = FindDescendants<UniformGrid>(_owner)
            .FirstOrDefault(g => g.Columns == 3 && ContainsText(g, "Windows Integrity") && ContainsText(g, "Windows Repair"));
        if (grid == null) return;

        foreach (var child in grid.Children.Cast<UIElement>().ToArray())
        {
            if (ContainsText(child, "تحديث التطبيقات") || ContainsText(child, "Restore Vault"))
                grid.Children.Remove(child);
        }

        if (grid.Children.Cast<FrameworkElement>().Any(x => Equals(x.Tag, "maintenance-center"))) return;
        var card = Card("maintenance-center", "Maintenance Center",
            "Apps/Drivers Plan • Startup • Background • Smart Removal • Restore Vault\nمحمي من التشغيل أثناء اللعب/البث.",
            () =>
            {
                var window = new MaintenanceCenterWindow { Owner = _owner, Icon = _owner.Icon };
                window.ShowDialog();
            });
        grid.Children.Insert(0, card);
    }

    private static Border Card(string tag, string title, string detail, Action open)
    {
        var border = new Border
        {
            Tag = tag,
            Background = Brush("Panel"),
            BorderBrush = Brush("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(15),
            Margin = new Thickness(4)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = detail, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 10), MinHeight = 36
        });
        var button = new Button { Content = "فتح", HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush("AccentStrong") };
        button.Click += (_, _) => open();
        stack.Children.Add(button);
        border.Child = stack;
        return border;
    }

    private static bool ContainsText(DependencyObject root, string value)
        => FindDescendants<TextBlock>(root).Any(x => x.Text.Equals(value, StringComparison.OrdinalIgnoreCase));

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Loaded -= OnLoaded;
        if (_devices != null) _devices.Click -= OnDevices;
        if (_updates != null) _updates.Click -= OnUpdates;
    }
}
