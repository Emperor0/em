using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class HudBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>((MainWindow)sender).InitializeHud()),true);
    }
}

public partial class MainWindow
{
    private bool _hudInjected;
    private GameOverlayWindow? _hud;
    private Button? _hudButton;

    internal void InitializeHud()
    {
        if(_hudInjected)return;_hudInjected=true;
        var sidebar=FindVisualChildren<StackPanel>(this).FirstOrDefault(stack=>stack.Children.OfType<Button>().Any(b=>string.Equals(b.Content?.ToString(),"التحديثات والإصلاح",StringComparison.Ordinal)));
        if(sidebar==null)return;
        var update=sidebar.Children.OfType<Button>().First(b=>string.Equals(b.Content?.ToString(),"التحديثات والإصلاح",StringComparison.Ordinal));
        var index=sidebar.Children.IndexOf(update);
        _hudButton=new Button{Content="تشغيل D7 HUD"};_hudButton.Click+=ToggleHud;sidebar.Children.Insert(index,_hudButton);
        Closed+=(_,_)=>{try{_hud?.Close();}catch{}};
    }

    private void ToggleHud(object sender,RoutedEventArgs e)
    {
        if(_hud!=null)
        {
            try{_hud.Close();}catch{} _hud=null;if(_hudButton!=null)_hudButton.Content="تشغيل D7 HUD";return;
        }
        var game=_orchestrator.LastStatus?.Context.PrimaryGame;
        if(string.IsNullOrWhiteSpace(game))
        {
            MessageBox.Show("D7 ما اكتشف لعبة شغالة الآن. افتح اللعبة أولًا وانتظر ثانيتين، ثم شغّل HUD.","D7 HUD");return;
        }
        _hud=new GameOverlayWindow(_hardware,game){Owner=this};
        _hud.Closed+=(_,_)=>{_hud=null;if(_hudButton!=null)_hudButton.Content="تشغيل D7 HUD";};
        _hud.Show();if(_hudButton!=null)_hudButton.Content="إيقاف D7 HUD";
    }
}
