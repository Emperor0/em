using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class ClipLibraryBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeClipLibrary()),
            true);
    }
}

public partial class MainWindow
{
    private bool _clipLibraryInjected;

    internal void InitializeClipLibrary()
    {
        if (_clipLibraryInjected) return;
        _clipLibraryInjected = true;

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;

        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);
        var button = new Button { Content = "مكتبة المقاطع" };
        button.Click += (_, _) =>
        {
            var window = new ClipLibraryWindow(() => _shadowCapture.LoadSettings().SaveFolder) { Owner = this };
            window.ShowDialog();
        };
        sidebar.Children.Insert(index, button);
    }
}
