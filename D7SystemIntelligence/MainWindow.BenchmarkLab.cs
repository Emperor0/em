using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class BenchmarkLabBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var window = (MainWindow)sender;
                window.Dispatcher.BeginInvoke(new Action(window.InitializeBenchmarkLab), DispatcherPriority.Loaded);
            }), true);
    }
}

public partial class MainWindow
{
    private bool _benchmarkLabInjected;
    private BenchmarkLabService? _benchmarkLab;

    internal void InitializeBenchmarkLab()
    {
        if (_benchmarkLabInjected) return;
        _benchmarkLabInjected = true;
        InitializeGameSessions();
        if (_gameSessions == null) return;
        _benchmarkLab = new BenchmarkLabService(_gameSessions);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;
        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);
        var button = new Button { Content = "Benchmark Lab" };
        button.Click += (_, _) =>
        {
            if (_benchmarkLab != null) new BenchmarkLabWindow(_benchmarkLab) { Owner = this }.ShowDialog();
        };
        sidebar.Children.Insert(index, button);
    }
}
