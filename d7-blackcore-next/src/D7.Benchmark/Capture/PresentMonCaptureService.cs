using System.Diagnostics;
using System.Text.Json;
using D7.Benchmark.Confidence;
using D7.Benchmark.Csv;
using D7.Benchmark.Models;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Tools.Acquisition;
using D7.Tools.Catalog;

namespace D7.Benchmark.Capture;

public sealed class PresentMonCaptureService : IFrameCaptureService
{
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly AppPaths _paths;
    private readonly JsonLineLogger _logger;
    private readonly ToolAcquisitionService _tools;

    public PresentMonCaptureService(AppPaths paths, JsonLineLogger logger, ToolAcquisitionService tools)
    {
        _paths = paths;
        _logger = logger;
        _tools = tools;
    }

    public async Task<BenchmarkCaptureResult> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (duration < TimeSpan.FromSeconds(5) || duration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(duration));

        var started = DateTimeOffset.UtcNow;
        var operationId = $"PM-{processId}-{started:yyyyMMddHHmmss}";
        var descriptor = OfficialToolCatalog.PresentMon;
        var tool = await _tools.EnsurePortableAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (!tool.Ready || string.IsNullOrWhiteSpace(tool.ExecutablePath))
        {
            return new BenchmarkCaptureResult(false, processId, started, duration, null, null, tool.MessageAr);
        }

        var directory = Path.Combine(_paths.Measurements, started.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(directory);
        var csvPath = Path.Combine(directory, "presentmon.csv");
        var seconds = Math.Max(5, (int)Math.Round(duration.TotalSeconds));
        var session = $"D7PM_{processId}_{started:HHmmssfff}";

        var startInfo = new ProcessStartInfo
        {
            FileName = tool.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = directory
        };
        startInfo.ArgumentList.Add("--process_id");
        startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--timed");
        startInfo.ArgumentList.Add(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--terminate_after_timed");
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add(csvPath);
        startInfo.ArgumentList.Add("--no_console_stats");
        startInfo.ArgumentList.Add("--v2_metrics");
        startInfo.ArgumentList.Add("--exclude_dropped");
        startInfo.ArgumentList.Add("--session_name");
        startInfo.ArgumentList.Add(session);

        await _logger.WriteAsync("Benchmark", operationId, "Information", "بدء قياس PresentMon.", new { processId, seconds, csvPath }, cancellationToken);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return new BenchmarkCaptureResult(false, processId, started, duration, null, null, "تعذر تشغيل محرك قياس الإطارات.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(duration + TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (cancellationToken.IsCancellationRequested) throw;
                return new BenchmarkCaptureResult(false, processId, started, duration, null, null, "تجاوز قياس الإطارات الوقت المتوقع وتم إيقافه بأمان.");
            }

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            _ = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (!File.Exists(csvPath))
            {
                await _logger.WriteAsync("Benchmark", operationId, "Error", "لم ينتج PresentMon ملف CSV.", new { process.ExitCode, stderr }, CancellationToken.None);
                return new BenchmarkCaptureResult(false, processId, started, duration, null, null, "لم ينتج محرك القياس بيانات صالحة.", stderr);
            }

            var analysis = await PresentMonCsvParser.ParseAsync(csvPath, cancellationToken).ConfigureAwait(false);
            var confidence = BenchmarkConfidenceEvaluator.Evaluate(analysis, duration);
            var manifestPath = await TryWriteManifestAsync(
                directory,
                new MeasurementManifest(
                    "D7.Measurement.v2",
                    started,
                    processId,
                    duration.TotalSeconds,
                    Environment.OSVersion.VersionString,
                    Environment.Version.ToString(),
                    descriptor.Version,
                    descriptor.Sha256,
                    descriptor.AssetName,
                    Path.GetFileName(csvPath),
                    analysis,
                    confidence),
                operationId,
                cancellationToken).ConfigureAwait(false);

            var captureValid = process.ExitCode == 0 && analysis.Valid;
            var success = captureValid && confidence.AutomaticDecisionAllowed;
            var message = !captureValid
                ? "تم إنشاء القياس لكن البيانات غير كافية أو غير متوافقة لإصدار حكم."
                : success
                    ? $"اكتمل قياس الأداء الحقيقي بنجاح. موثوقية القياس {confidence.LabelAr} ({confidence.Score}/100)."
                    : $"اكتمل القياس لكن موثوقيته {confidence.LabelAr} ({confidence.Score}/100)، لذلك لن يعتمد D7 عليه في قرار تلقائي.";

            await _logger.WriteAsync(
                "Benchmark",
                operationId,
                success ? "Information" : "Warning",
                message,
                new { process.ExitCode, captureValid, analysis, confidence, manifestPath },
                CancellationToken.None);
            return new BenchmarkCaptureResult(
                success,
                processId,
                started,
                duration,
                csvPath,
                analysis,
                message,
                string.IsNullOrWhiteSpace(stderr) ? null : stderr,
                manifestPath,
                confidence);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            await _logger.WriteAsync("Benchmark", operationId, "Error", "فشل قياس الإطارات.", new { error = ex.Message }, CancellationToken.None);
            return new BenchmarkCaptureResult(false, processId, started, duration, File.Exists(csvPath) ? csvPath : null, null, "تعذر إكمال قياس الإطارات دون التأثير على النظام.", ex.Message);
        }
    }

    private async Task<string?> TryWriteManifestAsync(
        string directory,
        MeasurementManifest manifest,
        string operationId,
        CancellationToken cancellationToken)
    {
        var target = Path.Combine(directory, "measurement.json");
        var temp = target + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, ManifestJson, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, target, true);
            return target;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            await _logger.WriteAsync(
                "Benchmark",
                operationId,
                "Warning",
                "اكتمل القياس لكن تعذر حفظ ملف بيانات إعادة الاختبار.",
                new { ex.Message },
                CancellationToken.None);
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
