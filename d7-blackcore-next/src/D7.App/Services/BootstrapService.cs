using System.IO;
using System.Net.Http;
using System.Security.Principal;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Core.Models;
using D7.Tools.Acquisition;
using D7.Tools.Catalog;

namespace D7.App.Services;

public sealed class BootstrapService
{
    private readonly AppPaths _paths;
    private readonly JsonLineLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly ToolAcquisitionService _tools;

    public BootstrapService(
        AppPaths paths,
        JsonLineLogger logger,
        HttpClient httpClient,
        ToolAcquisitionService tools)
    {
        _paths = paths;
        _logger = logger;
        _httpClient = httpClient;
        _tools = tools;
        _httpClient.Timeout = TimeSpan.FromSeconds(4);
    }

    public async Task<BootstrapResult> RunAsync(bool safeMode, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var checks = new List<BootstrapCheck>();
        const string op = "BOOTSTRAP";

        await _logger.WriteAsync("Bootstrap", op, "Information", "بدء تجهيز D7", new { safeMode }, cancellationToken);

        var os = Environment.OSVersion.Version;
        var isWindows = OperatingSystem.IsWindows();
        var isTargetBuildOrNewer = isWindows && os.Build >= 19045;
        checks.Add(new BootstrapCheck(
            "windows",
            "إصدار ويندوز",
            isTargetBuildOrNewer,
            true,
            isWindows
                ? $"Windows build {os.Build}"
                : "D7 BLACKCORE يعمل على Windows فقط."));

        try
        {
            _paths.EnsureCreated();
            var probe = Path.Combine(_paths.LocalDataRoot, ".write-probe");
            await File.WriteAllTextAsync(probe, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
            File.Delete(probe);
            checks.Add(new BootstrapCheck("storage", "مجلدات D7", true, true, "تم إنشاء مجلدات D7 والتحقق من الكتابة."));
        }
        catch (Exception ex)
        {
            checks.Add(new BootstrapCheck("storage", "مجلدات D7", false, true, "تعذر تجهيز مساحة تخزين D7."));
            await _logger.WriteAsync("Bootstrap", op, "Error", "فشل اختبار التخزين", new { error = ex.Message }, cancellationToken);
        }

        var elevated = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // Elevation is not a blocking bootstrap requirement; privileged writes will use a scoped helper later.
        }

        checks.Add(new BootstrapCheck(
            "privilege",
            "صلاحيات النظام",
            true,
            false,
            elevated ? "التطبيق يعمل حاليًا بصلاحية مسؤول." : "التطبيق يعمل بصلاحية عادية؛ العمليات الحساسة ستطلب صلاحيتها فقط عند الحاجة."));

        var online = false;
        try
        {
            using var response = await _httpClient.GetAsync("https://raw.githubusercontent.com/Emperor0/em/main/README.md", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            online = response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            online = false;
        }
        catch
        {
            online = false;
        }

        checks.Add(new BootstrapCheck(
            "internet",
            "الاتصال بالإنترنت",
            online,
            false,
            online ? "الاتصال متاح." : "الوضع الأساسي سيعمل دون إنترنت؛ التنزيلات والتحديثات ستنتظر عودة الاتصال."));

        try
        {
            var presentMon = await _tools.EnsurePortableAsync(OfficialToolCatalog.PresentMon, cancellationToken).ConfigureAwait(false);
            checks.Add(new BootstrapCheck(
                "presentmon",
                "محرك قياس الإطارات",
                presentMon.Ready,
                false,
                presentMon.Ready
                    ? "تم تجهيز محرك القياس والتحقق من سلامته تلقائيًا."
                    : presentMon.MessageAr));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            checks.Add(new BootstrapCheck(
                "presentmon",
                "محرك قياس الإطارات",
                false,
                false,
                online
                    ? "تعذر تجهيز محرك القياس الآن؛ سيعيد D7 المحاولة عند بدء القياس."
                    : "محرك القياس غير مخزن محليًا ويتطلب اتصالًا بالإنترنت لتجهيزه."));
            await _logger.WriteAsync("Bootstrap", op, "Warning", "تعذر تجهيز PresentMon أثناء الإقلاع.", new { ex.Message }, CancellationToken.None);
        }

        checks.Add(new BootstrapCheck(
            "safe-mode",
            "وضع الأمان",
            true,
            false,
            safeMode ? "D7 يعمل الآن في وضع الأمان بدون تحسينات تلقائية." : "وضع التشغيل الطبيعي فعال."));

        var result = new BootstrapResult(started, DateTimeOffset.UtcNow, safeMode, checks);
        await _logger.WriteAsync("Bootstrap", op, result.Ready ? "Information" : "Error", "انتهاء تجهيز D7", new { result.Ready, checks }, cancellationToken);
        return result;
    }
}
