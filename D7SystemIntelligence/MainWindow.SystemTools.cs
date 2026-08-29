using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class SystemToolsBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeSystemTools()), true);
    }
}

public partial class MainWindow
{
    private bool _systemToolsInjected;

    internal void InitializeSystemTools()
    {
        if(_systemToolsInjected)return;
        _systemToolsInjected=true;
        var sidebar=FindVisualChildren<StackPanel>(this).FirstOrDefault(stack=>stack.Children.OfType<Button>().Any(b=>string.Equals(b.Content?.ToString(),"التحديثات والإصلاح",StringComparison.Ordinal)));
        if(sidebar==null)return;
        var update=sidebar.Children.OfType<Button>().First(b=>string.Equals(b.Content?.ToString(),"التحديثات والإصلاح",StringComparison.Ordinal));
        var index=sidebar.Children.IndexOf(update);

        if(!sidebar.Children.OfType<Button>().Any(x=>Equals(x.Content,"Storage Center")))
        {
            var b=new Button{Content="Storage Center"}; b.Click+=(_,_)=>new StorageCenterWindow{Owner=this}.ShowDialog(); sidebar.Children.Insert(index++,b);
        }
        if(!sidebar.Children.OfType<Button>().Any(x=>Equals(x.Content,"Startup Manager")))
        {
            var b=new Button{Content="Startup Manager"}; b.Click+=(_,_)=>new StartupManagerWindow{Owner=this}.ShowDialog(); sidebar.Children.Insert(index,b);
        }
    }
}
