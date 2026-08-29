using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class PerformanceContractBootstrap
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
                window.Dispatcher.BeginInvoke(new Action(window.InitializePerformanceContract), DispatcherPriority.Loaded);
            }),
            true);
    }
}

public partial class MainWindow
{
    private bool _performanceContractInjected;
    private readonly PerformanceContractSettingsStore _performanceContractStore = new();
    private PerformanceContractService? _performanceContract;

    internal void InitializePerformanceContract()
    {
        if (_performanceContractInjected) return;
        _performanceContractInjected = true;
        InitializeGameSessions();
        if (_gameSessions == null) return;

        _performanceContract = new PerformanceContractService(_gameSessions, _hardware, _shadowCapture);
        _performanceContract.StatusChanged += message => Dispatcher.Invoke(() =>
        {
            if (message.StartsWith("CONTRACT", StringComparison.Ordinal)) StatusText.Text = message;
        });
        var saved = _performanceContractStore.Load();
        if (saved.Enabled) _performanceContract.Start(saved);

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar != null)
        {
            var update = sidebar.Children.OfType<Button>()
                .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
            var index = sidebar.Children.IndexOf(update);
            var button = new Button { Content = "Performance Contract" };
            button.Click += (_, _) =>
            {
                if (_performanceContract != null)
                    new PerformanceContractWindow(_performanceContract, _performanceContractStore) { Owner = this }.ShowDialog();
            };
            sidebar.Children.Insert(index, button);
        }

        Closed += (_, _) =>
        {
            try { _performanceContract?.Dispose(); } catch { }
        };
    }
}
