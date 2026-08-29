using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class StabilityToolsBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var window = (MainWindow)sender;
                window.Dispatcher.BeginInvoke(new Action(window.InitializeStabilityTools), DispatcherPriority.Loaded);
            }), true);
    }
}

public partial class MainWindow
{
    private bool _stabilityToolsInjected;

    internal void InitializeStabilityTools()
    {
        if (_stabilityToolsInjected) return;
        _stabilityToolsInjected = true;

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;
        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);

        var crash = new Button { Content = "Crash Investigator" };
        crash.Click += (_, _) => new CrashInvestigatorWindow { Owner = this }.ShowDialog();
        sidebar.Children.Insert(index++, crash);

        var restore = new Button { Content = "Restore Vault" };
        restore.Click += (_, _) => new RestoreVaultWindow { Owner = this }.ShowDialog();
        sidebar.Children.Insert(index, restore);
    }
}
