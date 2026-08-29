using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class StreamDirectorBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>((MainWindow)sender).InitializeStreamDirector()),true);
    }
}

public partial class MainWindow
{
    private bool _streamDirectorInjected;
    internal void InitializeStreamDirector()
    {
        if(_streamDirectorInjected)return;_streamDirectorInjected=true;
        var sidebar=FindVisualChildren<StackPanel>(this).FirstOrDefault(stack=>stack.Children.OfType<Button>().Any(b=>string.Equals(b.Content?.ToString(),"التحديثات والإصلاح",StringComparison.Ordinal)));
        if(sidebar==null)return;
        var update=sidebar.Children.OfType<Button>().First(b=>string.Equals(b.Content?.ToString(),"التحديثات والإصلاح",StringComparison.Ordinal));
        var index=sidebar.Children.IndexOf(update);
        var b=new Button{Content="Stream Director"};
        b.Click+=(_,_)=>new StreamDirectorWindow(_orchestrator.LastStatus?.Context.PrimaryGame){Owner=this}.ShowDialog();
        sidebar.Children.Insert(index,b);
    }
}
