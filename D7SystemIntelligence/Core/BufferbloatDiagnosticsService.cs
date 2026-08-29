using System.Net.Http;
using System.Net.NetworkInformation;

namespace D7SystemIntelligence.Core;

public sealed record BufferbloatReport(
    double? BaselineLatencyMs,
    double? LoadedLatencyMs,
    double? LoadedJitterMs,
    double? AddedLatencyMs,
    long DownloadedBytes,
    double DurationSeconds,
    string Verdict,
    string Detail);

public sealed class BufferbloatDiagnosticsService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const string PingTarget = "1.1.1.1";
    private const long DownloadBytes = 50_000_000;

    public async Task<BufferbloatReport> RunDownloadTestAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("قياس Latency بدون ضغط…");
        var baseline = await PingSeriesAsync(10, TimeSpan.FromMilliseconds(140), cancellationToken);
        if (baseline.Count < 4)
            return new(null, null, null, null, 0, 0, "غير مكتمل", "تعذر أخذ عينات Ping كافية قبل الاختبار.");

        progress?.Report("بدء Download load محدود حتى 50 MB وقياس Latency أثناءه…");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(12));
        var loadedSamples = new List<double>();
        var started = DateTime.UtcNow;
        long downloaded = 0;

        var pingTask = Task.Run(async () =>
        {
            using var ping = new Ping();
            while (!linked.IsCancellationRequested)
            {
                try
                {
                    var reply = await ping.SendPingAsync(PingTarget, 1000);
                    if (reply.Status == IPStatus.Success) lock (loadedSamples) loadedSamples.Add(reply.RoundtripTime);
                }
                catch { }
                try { await Task.Delay(100, linked.Token); } catch { break; }
            }
        }, linked.Token);

        try
        {
            using var response = await Http.GetAsync($"https://speed.cloudflare.com/__down?bytes={DownloadBytes}", HttpCompletionOption.ResponseHeadersRead, linked.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var buffer = new byte[128 * 1024];
            while (!linked.IsCancellationRequested && downloaded < DownloadBytes)
            {
                var read = await stream.ReadAsync(buffer, linked.Token);
                if (read <= 0) break;
                downloaded += read;
                if (downloaded % (5 * 1024 * 1024) < buffer.Length)
                    progress?.Report($"Download load… {downloaded / 1_000_000.0:0}/{DownloadBytes / 1_000_000} MB");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            linked.Cancel();
            try { await pingTask; } catch { }
            return new(baseline.Average(), null, null, null, downloaded, (DateTime.UtcNow - started).TotalSeconds, "فشل التحميل", "Cloudflare speed endpoint: " + ex.Message);
        }
        finally
        {
            linked.Cancel();
            try { await pingTask; } catch { }
        }

        double[] loaded;
        lock (loadedSamples) loaded = loadedSamples.ToArray();
        var baseAvg = baseline.Average();
        if (loaded.Length < 4)
            return new(baseAvg, null, null, null, downloaded, (DateTime.UtcNow - started).TotalSeconds, "عينات قليلة", "التحميل انتهى بسرعة كبيرة ولم نحصل على عينات loaded latency كافية. أعد الاختبار إذا احتجت نتيجة أدق.");

        var loadedAvg = loaded.Average();
        var jitter = MeanAbsoluteSuccessiveDifference(loaded);
        var delta = loadedAvg - baseAvg;
        var verdict = delta switch
        {
            <= 10 => "ممتاز — زيادة Latency منخفضة تحت Download load",
            <= 25 => "جيد — Bufferbloat محدود",
            <= 50 => "متوسط — الزيادة ملحوظة تحت الضغط",
            _ => "مرتفع — Bufferbloat واضح تحت Download load"
        };
        var detail = $"Baseline {baseAvg:0.0}ms → Loaded {loadedAvg:0.0}ms • Added {delta:+0.0;-0.0;0.0}ms • Loaded jitter {jitter:0.0}ms • Downloaded {downloaded / 1_000_000.0:0.0} MB.";
        return new(baseAvg, loadedAvg, jitter, delta, downloaded, (DateTime.UtcNow - started).TotalSeconds, verdict, detail);
    }

    private static async Task<List<double>> PingSeriesAsync(int count, TimeSpan delay, CancellationToken token)
    {
        using var ping = new Ping();
        var list = new List<double>();
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(PingTarget, 1000);
                if (r.Status == IPStatus.Success) list.Add(r.RoundtripTime);
            }
            catch { }
            if (i + 1 < count) await Task.Delay(delay, token);
        }
        return list;
    }

    private static double MeanAbsoluteSuccessiveDifference(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        double sum = 0;
        for (var i = 1; i < values.Count; i++) sum += Math.Abs(values[i] - values[i - 1]);
        return sum / (values.Count - 1);
    }
}
