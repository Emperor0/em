using D7SystemIntelligence.Core;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

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

        current.ReplaySeconds = seconds;
        current.SaveFolder = SaveFolderBox.Text.Trim();
        current.MaxLibraryGb = maxGb;
        current.AutoCleanup = true;
        current.SaveHotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? "F8" : HotkeyBox.Text.Trim().ToUpperInvariant();
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
                $"OBS: {(status.ObsRunning ? "يعمل" : "متوقف")} • WebSocket: {(status.Connected ? "متصل" : "غير متصل")} • Replay: {(status.ReplayActive ? "يعمل" : "متوقف")}\n" +
                (string.IsNullOrWhiteSpace(status.OutputMode) ? string.Empty : $"Output Mode: {status.OutputMode} • Encoder: {status.Encoder ?? "غير معروف"}\n") +
                $"المدة: {status.ReplaySeconds} ثانية • الحفظ: {status.SaveFolder}\n{status.Detail}";
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
            Title = "اختر مجلد حفظ مقاطع D7",
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
            ActionOutput.Text = "جاري تشغيل Replay Buffer الحقيقي…";
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
            ActionOutput.Text = "جاري حفظ آخر مقطع…";
            ActionOutput.Text = await _service.SaveReplayAsync();
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
            ActionOutput.Text = "فشل الإيقاف: " + ex.Message;
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
}
