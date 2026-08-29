using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

internal static class SafeMaintenanceBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeSafeMaintenance()),
            true);
    }
}

public partial class MainWindow
{
    private bool _safeMaintenanceInjected;
    private readonly SafeMaintenanceService _safeMaintenance = new();

    internal void InitializeSafeMaintenance()
    {
        if (_safeMaintenanceInjected) return;
        _safeMaintenanceInjected = true;

        var root = UpdatesPage.Children.OfType<StackPanel>().FirstOrDefault();
        if (root == null) return;

        var card = new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 6, 0, 14)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "UPDATE EVERYTHING SAFE", FontSize = 20, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = "يحدّث التطبيقات عبر Winget ثم تعريفات Windows Update بعد Backup + Restore Point. لا يلمس BIOS/Firmware، ولا يعمل أثناء اللعب.",
            Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10)
        });
        var status = new TextBlock { Text = "جاهز للفحص.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(status);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var scan = new Button { Content = "فحص فقط" };
        var run = new Button { Content = "تحديث كل شيء الآمن", Background = (Brush)FindResource("AccentSoft") };
        row.Children.Add(scan); row.Children.Add(run); stack.Children.Add(row); card.Child = stack;

        scan.Click += async (_, _) =>
        {
            if (IsGameBusy(status)) return;
            scan.IsEnabled = run.IsEnabled = false;
            status.Text = "جاري فحص التطبيقات والتعريفات…";
            try { status.Text = await _safeMaintenance.ScanOnlyAsync(); }
            catch (Exception ex) { status.Text = "فشل الفحص: " + ex.Message; }
            finally { scan.IsEnabled = run.IsEnabled = true; }
        };

        run.Click += async (_, _) =>
        {
            if (IsGameBusy(status)) return;
            if (MessageBox.Show("سيتم تحديث التطبيقات وتعريفات Windows Update. سيتم أخذ Driver Store Backup أولًا. المتابعة؟", "D7 Update Everything Safe", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            scan.IsEnabled = run.IsEnabled = false;
            var progress = new Progress<string>(x => status.Text = x);
            try
            {
                var result = await _safeMaintenance.RunUpdatesAsync(progress);
                status.Text = result.Detail + (result.RebootRequired ? "\n\nWindows يطلب إعادة تشغيل لإكمال بعض التعريفات." : string.Empty);
            }
            catch (Exception ex) { status.Text = "فشل Update Everything Safe: " + ex.Message; }
            finally { scan.IsEnabled = run.IsEnabled = true; }
        };

        root.Children.Insert(Math.Min(3, root.Children.Count), card);
    }

    private bool IsGameBusy(TextBlock status)
    {
        var mode = _orchestrator.LastStatus?.Context.Mode;
        if (mode is D7RuntimeMode.Gaming or D7RuntimeMode.StreamGaming)
        {
            status.Text = "D7 رفض التحديث لأن لعبة تعمل الآن. اقفل اللعبة ثم نفذ التحديث حتى لا نخاطر بالـstutter أو تبديل تعريف أثناء الجلسة.";
            return true;
        }
        return false;
    }
}
