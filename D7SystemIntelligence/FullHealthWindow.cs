using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class FullHealthWindow : Window
{
    private readonly FullHealthCheckService _service;
    private readonly CheckBox _integrity = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _output = new();

    public FullHealthWindow(FullHealthCheckService service)
    {
        _service = service;
        Title = "D7 NEXUS • Full Health Check";
        Width = 1020;
        Height = 740;
        MinWidth = 860;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "FULL HEALTH CHECK", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "هاردوير + حرارة/RAM/VRAM + Storage Reliability + Windows crashes/WHEA/GPU/Storage events. فحص DISM/SFC اختياري لأنه أطول.",
            Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 12) };
        _integrity.Content = "أضف DISM ScanHealth + SFC VerifyOnly";
        _integrity.VerticalAlignment = VerticalAlignment.Center;
        var run = new Button { Content = "تشغيل الفحص", MinWidth = 130, Background = (Brush)Application.Current.FindResource("AccentSoft") };
        run.Click += async (_, _) => await RunAsync(run);
        actions.Children.Add(_integrity); actions.Children.Add(run);
        Grid.SetRow(actions, 1); root.Children.Add(actions);

        var card = new Border { Background = (Brush)Application.Current.FindResource("AccentSoft"), BorderBrush = (Brush)Application.Current.FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0,0,0,12), Child = _status };
        _status.Text = "جاهز للفحص."; _status.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(card, 2); root.Children.Add(card);

        _output.IsReadOnly = true; _output.AcceptsReturn = true; _output.TextWrapping = TextWrapping.Wrap; _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; _output.Background = (Brush)Application.Current.FindResource("Panel"); _output.FontFamily = new FontFamily("Consolas"); _output.FontSize = 12.5;
        Grid.SetRow(_output, 3); root.Children.Add(_output);
        return root;
    }

    private async Task RunAsync(Button button)
    {
        button.IsEnabled = false;
        _output.Clear();
        var progress = new Progress<string>(x => _status.Text = x);
        try
        {
            var r = await _service.RunAsync(_integrity.IsChecked == true, progress);
            _status.Text = r.Summary;
            var lines = new List<string> { r.Summary, "", "=== Diagnostics ===" };
            lines.AddRange(r.Diagnostics.Select(x => $"[{x.Severity}] {x.Area} • {x.Title}\r\n{x.Detail}\r\n{x.Recommendation}"));
            lines.Add(""); lines.Add("=== Storage ==="); lines.Add(r.Storage.Summary);
            foreach (var d in r.Storage.Drives)
                lines.Add($"{d.FriendlyName} • {d.MediaType} • {d.HealthStatus} • {d.SizeGb:0}GB • Temp {(d.TemperatureC?.ToString("0") ?? "—")}° • PowerOn {(d.PowerOnHours?.ToString() ?? "—")}h • Wear {(d.Wear?.ToString() ?? "—")}");
            foreach (var v in r.Storage.Volumes)
                lines.Add($"{v.DriveLetter} • {v.HealthStatus} • Free {v.FreeGb:0.0}/{v.SizeGb:0.0}GB ({v.FreePercent:0.0}%)");
            lines.Add(""); lines.Add("=== Stability ==="); lines.Add(r.Stability.Verdict);
            lines.Add($"App {r.Stability.AppCrashes} • WHEA {r.Stability.HardwareErrors} • GPU {r.Stability.GpuDriverEvents} • Storage {r.Stability.StorageEvents} • Shutdown {r.Stability.UnexpectedShutdowns}");
            lines.Add(""); lines.Add("=== Windows Integrity ==="); lines.Add(r.WindowsIntegrity);
            _output.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) { _status.Text = "Full Health Check: " + ex.Message; }
        finally { button.IsEnabled = true; }
    }
}
