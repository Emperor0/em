using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace D7SystemIntelligence.Core;

public sealed record NetworkEndpointProbe(
    string Target,
    int SuccessfulSamples,
    double? AverageMs,
    double? JitterMs,
    double LossPercent,
    double? DnsResolutionMs,
    string Detail);

public sealed record NetworkDiagnosisReport(
    NetworkReport BaseReport,
    string LikelyLayer,
    string Verdict,
    double? DnsResolutionMs,
    NetworkEndpointProbe? RemoteEndpoint,
    IReadOnlyList<string> Evidence);

public sealed class NetworkIntelligence
{
    public async Task<NetworkReport> ScanAsync(CancellationToken cancellationToken = default)
    {
        var nic = ActiveNic();
        if (nic == null)
            return new NetworkReport("غير متصل", "غير متاح", 0, null, null, null, 100, "لم يتم العثور على اتصال شبكة نشط ببوابة IPv4.");

        var props = nic.GetIPProperties();
        var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "غير متاح";
        var gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
        var dns = string.Join("، ", props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()));

        PingStats? gatewayStats = null;
        if (gateway != null) gatewayStats = await MeasureAsync(gateway.ToString(), 8, cancellationToken);

        var cloudflare = await MeasureAsync("1.1.1.1", 10, cancellationToken);
        var google = await MeasureAsync("8.8.8.8", 10, cancellationToken);
        var internet = ChooseBest(cloudflare, google);

        var notes = new List<string>();
        if (internet.LossPercent > 0) notes.Add($"فقد حزم {internet.LossPercent:0.#}%");
        if (internet.JitterMs is > 8) notes.Add($"تذبذب مرتفع {internet.JitterMs:0.0} ms");
        if (gatewayStats?.AverageMs is > 5) notes.Add($"زمن البوابة مرتفع {gatewayStats.AverageMs:0.0} ms");
        if (cloudflare.SuccessCount > 0 && google.SuccessCount > 0 &&
            Math.Abs((cloudflare.AverageMs ?? 0) - (google.AverageMs ?? 0)) >= 20)
            notes.Add("يوجد فرق كبير بين مسارين خارجيين؛ قد تكون المشكلة Routing وليست من الكمبيوتر نفسه");
        if (notes.Count == 0) notes.Add("الاتصال الأساسي مستقر حسب الاختبار الحالي");

        return new NetworkReport(
            nic.Name,
            ipv4,
            nic.Speed,
            internet.AverageMs,
            internet.JitterMs,
            gatewayStats?.AverageMs,
            internet.LossPercent,
            $"DNS: {(string.IsNullOrWhiteSpace(dns) ? "غير متاح" : dns)} • {string.Join(" • ", notes)}");
    }

    public async Task<NetworkDiagnosisReport> DiagnoseAsync(string? remoteHost = null, CancellationToken cancellationToken = default)
    {
        var report = await ScanAsync(cancellationToken);
        var evidence = new List<string>();
        if (report.AdapterName == "غير متصل")
            return new(report, "PC / NIC", "لا يوجد مسار IPv4 نشط ببوابة افتراضية.", null, null, ["Windows لم يعرض NIC نشطًا مع Default Gateway."]);

        var dnsMs = await MeasureDnsAsync(cancellationToken);
        if (dnsMs.HasValue) evidence.Add($"DNS resolution {dnsMs:0.0}ms");
        if (report.GatewayLatencyMs.HasValue) evidence.Add($"Gateway {report.GatewayLatencyMs:0.0}ms");
        if (report.InternetLatencyMs.HasValue) evidence.Add($"Internet {report.InternetLatencyMs:0.0}ms");
        evidence.Add($"Jitter {(report.JitterMs.HasValue ? report.JitterMs.Value.ToString("0.0") + "ms" : "—")} • Loss {report.PacketLossPercent:0.#}%");

        NetworkEndpointProbe? remote = null;
        if (!string.IsNullOrWhiteSpace(remoteHost))
            remote = await ProbeEndpointAsync(remoteHost.Trim(), cancellationToken);

        var layer = "Healthy / غير محدد";
        var verdict = "المسار العام يبدو مستقرًا. إذا كانت المشكلة داخل لعبة واحدة فقط، اختبر Host/IP للسيرفر أو اعتبر Route/Server نفسه قبل تعديل Windows.";

        if (report.GatewayLatencyMs is > 8)
        {
            layer = "Router / Wi‑Fi / Local Link";
            verdict = "الـLatency مرتفع قبل الخروج للإنترنت؛ افحص Wi‑Fi/الكابل/الراوتر/NIC أولًا. تغيير DNS أو اللعبة لن يصلح هذا السبب.";
        }
        else if (report.PacketLossPercent >= 2 || report.JitterMs is > 12)
        {
            layer = "ISP / Upstream Route";
            verdict = "البوابة المحلية جيدة لكن الإنترنت العام يظهر Loss/Jitter؛ الاحتمال الأقوى ISP أو المسار الخارجي، وليس Tweaks ويندوز.";
        }
        else if (dnsMs is > 250 && report.InternetLatencyMs is < 100)
        {
            layer = "DNS";
            verdict = "الوصول بالـIP طبيعي نسبيًا لكن حل الأسماء بطيء. هنا تغيير DNS قد يكون له فائدة فعلية في فتح الخدمات، لكنه لا يخفض Ping داخل جلسة لعبة قائمة.";
        }

        if (remote != null)
        {
            evidence.Add($"Remote {remote.Target}: {Fmt(remote.AverageMs)} • jitter {Fmt(remote.JitterMs)} • loss {remote.LossPercent:0.#}%");
            var generalHealthy = report.GatewayLatencyMs is <= 8 && report.PacketLossPercent < 2 && report.JitterMs is <= 12;
            var remoteBad = remote.LossPercent >= 2 || remote.JitterMs is > 15 ||
                            (remote.AverageMs.HasValue && report.InternetLatencyMs.HasValue && remote.AverageMs.Value >= report.InternetLatencyMs.Value + 60);
            if (generalHealthy && remoteBad)
            {
                layer = "Game Server / Remote Route";
                verdict = "شبكتك العامة مستقرة لكن الهدف المحدد أسوأ بوضوح؛ لا توجد إشارة تبرر تعديل NIC. الاحتمال الأقوى السيرفر/المسار إليه/منطقته.";
            }
        }

        return new NetworkDiagnosisReport(report, layer, verdict, dnsMs, remote, evidence);
    }

    public async Task<NetworkEndpointProbe> ProbeEndpointAsync(string target, CancellationToken cancellationToken = default)
    {
        var dnsWatch = Stopwatch.StartNew();
        string pingTarget = target;
        double? dnsMs = null;
        try
        {
            if (!IPAddress.TryParse(target, out _))
            {
                var addresses = await Dns.GetHostAddressesAsync(target, cancellationToken);
                dnsWatch.Stop();
                dnsMs = dnsWatch.Elapsed.TotalMilliseconds;
                var ipv4 = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 != null) pingTarget = ipv4.ToString();
            }
        }
        catch (Exception ex)
        {
            return new NetworkEndpointProbe(target, 0, null, null, 100, null, "تعذر Resolve للهدف: " + ex.Message);
        }

        var stats = await MeasureAsync(pingTarget, 12, cancellationToken);
        return new NetworkEndpointProbe(
            target,
            stats.SuccessCount,
            stats.AverageMs,
            stats.JitterMs,
            stats.LossPercent,
            dnsMs,
            stats.SuccessCount == 0
                ? "الهدف لم يرد على ICMP. هذا لا يثبت أنه Offline؛ بعض السيرفرات تمنع Ping."
                : $"{stats.SuccessCount}/12 ردود ICMP.");
    }

    private static NetworkInterface? ActiveNic()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();

    private static async Task<double?> MeasureDnsAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await Dns.GetHostAddressesAsync("www.cloudflare.com", cancellationToken);
            sw.Stop();
            return result.Length > 0 ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch { return null; }
    }

    private static PingStats ChooseBest(PingStats a, PingStats b)
    {
        if (a.SuccessCount == 0) return b;
        if (b.SuccessCount == 0) return a;
        return (a.AverageMs ?? double.MaxValue) <= (b.AverageMs ?? double.MaxValue) ? a : b;
    }

    private static async Task<PingStats> MeasureAsync(string host, int samples, CancellationToken cancellationToken)
    {
        var values = new List<long>();
        using var ping = new Ping();

        for (var i = 0; i < samples; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, 900);
                if (reply.Status == IPStatus.Success) values.Add(reply.RoundtripTime);
            }
            catch { }
            await Task.Delay(70, cancellationToken);
        }

        var loss = samples == 0 ? 100 : (samples - values.Count) * 100.0 / samples;
        if (values.Count == 0) return new PingStats(host, 0, null, null, loss);

        double jitter = 0;
        if (values.Count > 1)
            jitter = values.Zip(values.Skip(1), (a, b) => Math.Abs(a - b)).Average();

        return new PingStats(host, values.Count, values.Average(), jitter, loss);
    }

    private static string Fmt(double? value) => value.HasValue ? $"{value.Value:0.0}ms" : "—";

    private sealed record PingStats(string Host, int SuccessCount, double? AverageMs, double? JitterMs, double LossPercent);
}
