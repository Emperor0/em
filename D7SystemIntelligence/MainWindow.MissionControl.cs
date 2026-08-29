using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class MissionControlBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeMissionControl()),
            true);
    }
}

public partial class MainWindow
{
    private bool _missionControlInjected;
    private D7MissionEngine? _missionEngine;

    internal void InitializeMissionControl()
    {
        if (_missionControlInjected) return;
        _missionControlInjected = true;
        _missionEngine = new D7MissionEngine(_hardware);
        _missionEngine.StatusChanged += message => Dispatcher.Invoke(() => StatusText.Text = message);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;

        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);
        var button = new Button { Content = "Mission Control" };
        button.Click += (_, _) =>
        {
            if (_missionEngine == null) return;
            new MissionControlWindow(
                _missionEngine,
                () => _orchestrator.LastStatus?.Context.PrimaryGame)
            { Owner = this }.ShowDialog();
        };
        sidebar.Children.Insert(index, button);

        Closed += async (_, _) =>
        {
            if (_missionEngine != null)
            {
                try { await _missionEngine.DisposeAsync(); } catch { }
            }
        };
    }
}
