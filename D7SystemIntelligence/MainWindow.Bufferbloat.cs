using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace D7SystemIntelligence;

internal static class BufferbloatBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                var window = (MainWindow)sender;
                window.Dispatcher.BeginInvoke(new Action(window.InitializeBufferbloatTest), DispatcherPriority.ContextIdle);
            }), true);
    }
}

public partial class MainWindow
{
    private bool _bufferbloatInjected;
    private readonly BufferbloatDiagnosticsService _bufferbloat = new();

    internal void InitializeBufferbloatTest()
    {
        if (_bufferbloatInjected) return;
        _bufferbloatInjected = true;
        var scan = NetworkPage.Children.OfType<Button>()
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), "فحص الشبكة الآن", StringComparison.Ordinal));
        if (scan == null) return;

        NetworkPage.Children.Remove(scan);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 16) };
        row.Children.Add(scan);
        var test = new Button { Content = "اختبار Bufferbloat" };
        test.Click += async (_, _) =>
        {
            var mode = _orchestrator.LastStatus?.Context.Mode;
            if (mode is D7RuntimeMode.Gaming or D7RuntimeMode.StreamGaming)
            {
                NetworkNotesText.Text = "D7 رفض Bufferbloat Test أثناء اللعب لأنه يولد Download load متعمد.";
                return;
            }
            if (MessageBox.Show("الاختبار ينزل حتى 50 MB تقريبًا من Cloudflare لقياس زيادة البنق تحت الضغط. المتابعة؟", "D7 Bufferbloat", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            test.IsEnabled = scan.IsEnabled = false;
            var progress = new Progress<string>(x => NetworkNotesText.Text = x);
            try
            {
                var r = await _bufferbloat.RunDownloadTestAsync(progress);
                NetworkNotesText.Text = r.Verdict + "\n" + r.Detail + "\nهذا اختبار Download-load فقط؛ لا يدّعي قياس Upload bufferbloat.";
            }
            catch (Exception ex) { NetworkNotesText.Text = "Bufferbloat Test: " + ex.Message; }
            finally { test.IsEnabled = scan.IsEnabled = true; }
        };
        row.Children.Add(test);
        Grid.SetRow(row, 1);
        NetworkPage.Children.Add(row);
    }
}
