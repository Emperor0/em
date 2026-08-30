using System.Windows;

namespace D7SystemIntelligence;

public partial class App : Application
{
    private AppIntelligenceIntegration? _appIntelligence;
    private NetworkLabIntegration? _networkLab;
    private ShellConsolidationIntegration? _shellConsolidation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(x => x.Equals("--post-update-healthcheck", StringComparison.OrdinalIgnoreCase)))
        {
            RunPostUpdateHealthCheck();
            return;
        }

        var window = new D7KtShellWindow();
        _appIntelligence = AppIntelligenceIntegration.Attach(window);
        _networkLab = NetworkLabIntegration.Attach(window);
        _shellConsolidation = ShellConsolidationIntegration.Attach(window);
        MainWindow = window;
        window.Show();
    }

    private void RunPostUpdateHealthCheck()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "UpdateRecovery");
        Directory.CreateDirectory(root);
        var log = Path.Combine(root, "healthcheck.log");
        try
        {
            var updater = new Core.D7UpdateService();
            var icon = D7KtBrand.CreateIcon();
            if (icon == null) throw new InvalidOperationException("D7KT brand resources failed to initialize.");

            var shell = new D7KtShellWindow();
            ShellContractValidator.Validate(shell);
            shell.Close();

            File.WriteAllText(log,
                $"HEALTHY\r\nVersion={updater.CurrentVersionText}\r\nAt={DateTimeOffset.Now:O}\r\nShellConstruction=OK\r\nShellContract=6-CENTERS\r\n");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(log,
                    $"FAILED\r\nAt={DateTimeOffset.Now:O}\r\n{ex}\r\n");
            }
            catch { }
            Shutdown(20);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shellConsolidation?.Dispose();
        _shellConsolidation = null;
        _networkLab?.Dispose();
        _networkLab = null;
        _appIntelligence?.Dispose();
        _appIntelligence = null;
        base.OnExit(e);
    }
}
