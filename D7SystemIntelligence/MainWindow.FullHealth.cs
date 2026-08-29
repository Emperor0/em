using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class FullHealthBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var window = (MainWindow)sender;
                window.Dispatcher.BeginInvoke(new Action(window.InitializeFullHealth), DispatcherPriority.Loaded);
            }), true);
    }
}

public partial class MainWindow
{
    private bool _fullHealthInjected;
    private FullHealthCheckService? _fullHealth;

    internal void InitializeFullHealth()
    {
        if (_fullHealthInjected) return;
        _fullHealthInjected = true;
        _fullHealth = new FullHealthCheckService(_hardware);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;
        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);
        var button = new Button { Content = "Full Health Check" };
        button.Click += (_, _) =>
        {
            if (_fullHealth != null) new FullHealthWindow(_fullHealth) { Owner = this }.ShowDialog();
        };
        sidebar.Children.Insert(index, button);
    }
}
