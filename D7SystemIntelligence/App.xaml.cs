using System.Windows;

namespace D7SystemIntelligence;

public partial class App : Application
{
    private AppIntelligenceIntegration? _appIntelligence;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new D7KtShellWindow();
        _appIntelligence = AppIntelligenceIntegration.Attach(window);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appIntelligence?.Dispose();
        _appIntelligence = null;
        base.OnExit(e);
    }
}
