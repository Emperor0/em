using System.Windows;

namespace D7SystemIntelligence;

public partial class App : Application
{
    private AppIntelligenceIntegration? _appIntelligence;
    private NetworkLabIntegration? _networkLab;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new D7KtShellWindow();
        _appIntelligence = AppIntelligenceIntegration.Attach(window);
        _networkLab = NetworkLabIntegration.Attach(window);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _networkLab?.Dispose();
        _networkLab = null;
        _appIntelligence?.Dispose();
        _appIntelligence = null;
        base.OnExit(e);
    }
}
