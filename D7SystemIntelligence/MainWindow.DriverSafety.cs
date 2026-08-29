using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class DriverSafetyBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeDriverSafety()), true);
    }
}

public partial class MainWindow
{
    private bool _driverSafetyInjected;

    internal void InitializeDriverSafety()
    {
        if (_driverSafetyInjected) return;
        _driverSafetyInjected = true;

        var scan = DriversPage.Children.OfType<Button>()
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), "فحص التعريفات", StringComparison.Ordinal));
        if (scan != null)
        {
            DriversPage.Children.Remove(scan);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 16) };
            row.Children.Add(scan);
            var safety = new Button { Content = "Driver Safety Center" };
            safety.Click += (_, _) => new DriverSafetyWindow { Owner = this }.ShowDialog();
            row.Children.Add(safety);
            Grid.SetRow(row, 1);
            DriversPage.Children.Add(row);
        }
    }
}
