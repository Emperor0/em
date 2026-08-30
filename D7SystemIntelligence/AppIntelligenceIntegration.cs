using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

internal sealed class AppIntelligenceIntegration : IDisposable
{
    private readonly AppIntelligenceService _service = new();
    private readonly Window _owner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private D7Mission _lastMission = (D7Mission)(-1);
    private bool _disposed;

    private AppIntelligenceIntegration(Window owner)
    {
        _owner = owner;
        D7RuntimeBus.Changed += OnRuntimeChanged;
        owner.Loaded += OnLoaded;
        owner.Closed += (_, _) => Dispose();
    }

    public static AppIntelligenceIntegration Attach(Window owner) => new(owner);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InjectNavigationButton();
        _ = ApplyMissionProfileAsync(D7RuntimeBus.Mission);
    }

    private void InjectNavigationButton()
    {
        var devices = FindDescendants<Button>(_owner).FirstOrDefault(x => Equals(x.Tag, "devices"));
        if (devices?.Parent is not StackPanel panel) return;
        if (panel.Children.OfType<Button>().Any(x => Equals(x.Tag, "app-intelligence"))) return;

        var button = new Button
        {
            Tag = "app-intelligence",
            Margin = new Thickness(0, 3, 0, 3),
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new TextBlock
        {
            Text = "◇",
            FontSize = 17,
            Foreground = Brush("Accent"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "البرامج الذكية", FontSize = 13.3, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = "Discord • Steam • NVIDIA • Apps", FontSize = 9.3, Foreground = Brush("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        button.Content = grid;
        button.Click += (_, _) =>
        {
            var window = new AppIntelligenceWindow { Owner = _owner, Icon = _owner.Icon };
            window.ShowDialog();
        };

        var index = panel.Children.IndexOf(devices) + 1;
        panel.Children.Insert(index, button);
    }

    private async void OnRuntimeChanged()
    {
        if (_disposed) return;
        var mission = D7RuntimeBus.Mission;
        if (mission == _lastMission) return;
        _lastMission = mission;
        await ApplyMissionProfileAsync(mission);
    }

    private async Task ApplyMissionProfileAsync(D7Mission mission)
    {
        if (_disposed || !await _gate.WaitAsync(0)) return;
        try
        {
            if (mission == D7Mission.None || mission == D7Mission.Silent)
            {
                foreach (var id in ManagedIds)
                    await _service.RestoreProfileAsync(id, silentWhenMissing: true);
                return;
            }

            var mode = mission == D7Mission.StreamRanked ? AppProfileMode.Streaming : AppProfileMode.Gaming;
            foreach (var id in ManagedIds)
            {
                try { await _service.ApplyProfileAsync(id, mode); }
                catch { }
            }
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        D7RuntimeBus.Changed -= OnRuntimeChanged;
        _owner.Loaded -= OnLoaded;
        _gate.Dispose();
    }

    private static readonly ManagedAppId[] ManagedIds =
    [
        ManagedAppId.Discord,
        ManagedAppId.Steam,
        ManagedAppId.NvidiaApp,
        ManagedAppId.Obs,
        ManagedAppId.TikTokLiveStudio,
        ManagedAppId.Chrome,
        ManagedAppId.Edge
    ];

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
