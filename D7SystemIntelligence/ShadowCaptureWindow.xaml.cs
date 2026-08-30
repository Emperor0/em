using D7SystemIntelligence.Core;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace D7SystemIntelligence;

public partial class ShadowCaptureWindow : Window
{
    private readonly ShadowCaptureService _service;

    public ShadowCaptureWindow(ShadowCaptureService service)
    {
        InitializeComponent();
        _service = service;
        LoadSettingsIntoUi();
        Loaded += async (_, _) => await RefreshStatusCore();
    }

    private void LoadSettingsIntoUi()
    {
        var settings = _service.LoadSettings();
        ReplaySecondsBox.Text = settings.ReplaySeconds.ToString();
        SaveFolderBox.Text = settings.SaveFolder;
        MaxLibraryBox.Text = settings.MaxLibraryGb.ToString();
        HotkeyBox.Text = settings.SaveHotkey;
        GameFoldersBox.IsChecked = settings.UseGameSubfolders;
        AutoNameBox.IsChecked = settings.AutoNameWithGame;
        MetadataBox.IsChecked = settings.CreateMetadataSidecar;
        ProtectPerformanceBox.IsChecked = settings.ProtectPerformance;
        ImpactSecondsBox.Text = settings.ImpactTestSeconds.ToString();
        MaxFpsLossBox.Text = settings.MaxFpsLoss.ToString();
        MaxGpuBudgetBox.Text = settings.MaxGpuBudgetPercent.ToString();
        ObsHostBox.Text = settings.ObsHost;
        ObsPortBox.Text = settings.ObsPort.ToString();
        AutoStartObsBox.IsChecked = settings.AutoStartObs;
        ObsPasswordBox.Password = string.Empty;
    }

    private ShadowCaptureSettings ReadSettingsFromUi()
    {
        var current = _service.LoadSettings();

        if (!int.TryParse(ReplaySecondsBox.Text.Trim(), out var seconds))
            throw new InvalidOperationException("مدة المقطع غير صحيحة.");
        if (!int.TryParse(MaxLibraryBox.Text.Trim(), out var maxGb))
            throw new InvalidOperationException("حجم مكتبة المقاطع غير صحيح.");
        if (!int.TryParse(ObsPortBox.Text.Trim(), out var port))
            throw new InvalidOperationException("منفذ OBS WebSocket غير صحيح.");
        if (!int.TryParse(ImpactSecondsBox.Text.Trim(), out var impactSeconds))
            throw new InvalidOperationException("مدة Impact Test غير صحيحة.");
        if (!int.TryParse(MaxFpsLossBox.Text.Trim(), out var maxFpsLoss))
            throw new InvalidOperationException("حد خسارة FPS غير صحيح.");
        if (!int.TryParse(MaxGpuBudgetBox.Text.Trim(), out var maxGpuBudget))
            throw new InvalidOperationException("ميزانية GPU غير صحيحة.");

        current.ReplaySeconds = seconds;
        current.SaveFolder = SaveFolderBox.Text.Trim();
        current.MaxLibraryGb = maxGb;
        current.AutoCleanup = true;
        current.SaveHotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? "F8" : HotkeyBox.Text.Trim().ToUpperInvariant();
        current.UseGameSubfolders = GameFoldersBox.IsChecked == true;
        current.AutoNameWithGame = AutoNameBox.IsChecked == true;
        current.CreateMetadataSidecar = MetadataBox.IsChecked == true;
        current.ProtectPerformance = ProtectPerformanceBox.IsChecked == true;
        current.ImpactTestSeconds = impactSeconds;
        current.MaxFpsLoss = maxFpsLoss;
        current.MaxGpuBudgetPercent = maxGpuBudget;
        current.ObsHost = ObsHostBox.Text.Trim();
        current.ObsPort = port;
        current.AutoStartObs = AutoStartObsBox.IsChecked == true;
        current.KeepReplayRunning = true;
        return current;
    }

    private void PersistSettings()
    {
        var settings = ReadSettingsFromUi();
        var password = string.IsNullOrEmpty(ObsPasswordBox.Password) ? null : ObsPasswordBox.Password;
        _service.SaveSettings(settings, password);
        ObsPasswordBox.Password = string.Empty;
        ActionOutput.Text = "تم حفظ إعدادات Shadow Capture.";
    }

    private async void RefreshStatus(object sender, RoutedEventArgs e)
        => await RefreshStatusCore();

    private async Task RefreshStatusCore()
    {
        SetBusy(true);
        try
        {
            var status = await _service.GetStatusAsync();
            StatusText.Text =
                $"OBS: {(status.ObsRunning ? "يعمل" : "متوقف")} • WebSocket: {(status.Connected ? "متصل" : "غير متصل")} • Replay: {(status.ReplayActive ? "يعمل" : "متوقف")} • Stream: {(status.StreamActive ? "ON" : "OFF")} • Record: {(status.RecordActive ? "ON" : "OFF")}\n" +
                (string.IsNullOrWhiteSpace(status.OutputMode) ? string.Empty : $"Output: {status.OutputMode} • Encoder: {status.Encoder ?? "غير معروف"}\n") +
                $"OBS CPU {Fmt(status.ObsCpuUsage)}% • OBS FPS {Fmt(status.ObsActiveFps)} • Render skip {Fmt(status.RenderSkipPercent)}% • Output skip {Fmt(status.OutputSkipPercent)}%\n" +
                $"Game FPS {Fmt(status.GameFps)} • 1% {Fmt(status.GameOnePercentLow)} • P99 {Fmt(status.GameP99FrameMs)}ms • GPU {Fmt(status.GpuLoad)}%\n" +
                $"المدة: {status.ReplaySeconds}s • مكتبة D7KT: {status.SaveFolder}\n{status.Detail}";

            HealthText.Text = $"Health: {status.Health}" +
                              (string.IsNullOrWhiteSpace(status.DuplicateCaptureWarning) ? string.Empty : $"\n{status.DuplicateCaptureWarning}");
            HealthText.Foreground = status.Health switch
            {
                "Good" => ResourceBrush("Success", Brushes.LightGreen),
                "Warning" => ResourceBrush("Warning", Brushes.Gold),
                "Unavailable" => ResourceBrush("Muted", Brushes.Gray),
                _ => ResourceBrush("Text", Brushes.White)
            };
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "اختر مجلد حفظ مقاطع D7KT",
            InitialDirectory = Directory.Exists(SaveFolderBox.Text) ? SaveFolderBox.Text : null,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            SaveFolderBox.Text = dialog.FolderName;
    }

    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistSettings();
        }
        catch (Exception ex)
        {
            ActionOutput.Text = "تعذر حفظ الإعدادات: " + ex.Message;
        }
    }

    private async void StartCapture(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            PersistSettings();
            ActionOutput.Text = "جاري تشغيل/اعتماد Replay Buffer…";
            ActionOutput.Text = await _service.StartAsync();
            await RefreshStatusCore();
        }
        catch (Exception ex)
        {
            ActionOutput.Text = "فشل التشغيل: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveReplayNow(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            PersistSettings();
            ActionOutput.Text = "جاري حفظ وتنظيم آخر مقطع…";
            ActionOutput.Text = await _service.SaveReplayAsync();
            await RefreshStatusCore();
        }
        catch (Exception ex)
        {
            ActionOutput.Text = "فشل حفظ المقطع: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void StopCapture(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            ActionOutput.Text = await _service.StopAsync();
            await RefreshStatusCore();
        }
        catch (Exception ex)
        {
            ActionOutput.Text = "فشل الإيقاف/الاستعادة: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RunImpactTest(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            PersistSettings();
            ImpactResultText.Text = "Baseline → تشغيل Replay → قياس → استعادة OBS… لا تغيّر المشهد داخل اللعبة قدر الإمكان.";
            var result = await _service.RunImpactCheckAsync();
            ImpactResultText.Text = result.Verdict;
            ImpactResultText.Foreground = !result.Performed
                ? ResourceBrush("Muted", Brushes.Gray)
                : result.Passed ? ResourceBrush("Success", Brushes.LightGreen) : ResourceBrush("Warning", Brushes.Gold);
            await RefreshStatusCore();
        }
        catch (Exception ex)
        {
            ImpactResultText.Text = "Impact Test فشل: " + ex.Message;
            ImpactResultText.Foreground = ResourceBrush("Danger", Brushes.OrangeRed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenClipsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = ReadSettingsFromUi().SaveFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ActionOutput.Text = "تعذر فتح المجلد: " + ex.Message;
        }
    }

    private void ClearObsPassword(object sender, RoutedEventArgs e)
    {
        WindowsCredentialStore.Delete(ShadowCaptureService.ObsCredentialTarget);
        ObsPasswordBox.Password = string.Empty;
        ActionOutput.Text = "تم مسح كلمة مرور OBS المحفوظة من Windows Credential Manager.";
    }

    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
    }

    private static string Fmt(double? value) => value.HasValue ? value.Value.ToString("0.00") : "—";

    private static Brush ResourceBrush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;
}
