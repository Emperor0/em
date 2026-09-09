using System.Diagnostics;
using System.Windows.Forms;
using D7.Updater.Activation;

namespace D7.Launcher;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var installRoot = Path.GetFullPath(AppContext.BaseDirectory);
            var store = new VersionPointerStore(installRoot);
            var pointer = await store.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (pointer is null)
            {
                ShowError("تعذر العثور على النسخة النشطة من D7. استخدم خيار الإصلاح من مثبت D7 BLACKCORE.");
                return;
            }

            var executable = store.ResolveExecutablePath(pointer);
            if (!File.Exists(executable))
            {
                ShowError("ملفات النسخة النشطة من D7 غير مكتملة. استخدم خيار الإصلاح من مثبت D7 BLACKCORE.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? installRoot,
                UseShellExecute = false
            };

            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (Process.Start(startInfo) is null)
            {
                ShowError("تعذر تشغيل D7 BLACKCORE من النسخة النشطة.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"تعذر تشغيل D7 BLACKCORE بأمان.\n\nالتفاصيل التقنية: {ex.Message}");
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "D7 BLACKCORE — المشغل",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }
}
