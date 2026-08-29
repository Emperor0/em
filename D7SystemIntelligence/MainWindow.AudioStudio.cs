using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class AudioStudioBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeAudioStudio()),
            true);
    }
}

public partial class MainWindow
{
    private bool _audioStudioInjected;

    internal void InitializeAudioStudio()
    {
        if (_audioStudioInjected) return;
        _audioStudioInjected = true;

        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null || sidebar.Children.OfType<Button>().Any(x => Equals(x.Content, "Audio Studio"))) return;

        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(update);
        var audio = new Button { Content = "Audio Studio" };
        audio.Click += (_, _) => new AudioStudioWindow { Owner = this }.ShowDialog();
        sidebar.Children.Insert(index, audio);
    }
}
