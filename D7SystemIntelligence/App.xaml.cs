using System.Windows;

namespace D7SystemIntelligence;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new D7KtShellWindow();
        MainWindow = window;
        window.Show();
    }
}
