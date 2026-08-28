using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace D7SystemIntelligence.Core;

public sealed class NetworkIntelligence
{
    public async Task<NetworkReport> ScanAsync(CancellationToken cancellationToken = default)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();

        if (nic == null)
            return new NetworkReport("غير متصل", "غير متاح", 0, null, null, null, 100, "لم يتم العثور على اتصال شبكة نشط ببوابة IPv4.");

        var props = nic.GetIPProperties();
        var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "غير متاح";
        var gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
        var dns = string.Join("، ", props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()));

        PingStats? gatewayStats = null;
        if (gateway != null) gatewayStats = await MeasureAsync(gateway.ToString(), 6, cancellationToken);

        var cloudflare = await MeasureAsync("1.1.1.1", 8, cancellationToken);
        var google = await MeasureAsync("8.8.8.8", 8, cancellationToken);
        var internet = ChooseBest(cloudflare, google);

        var notes = new List<string>();
        if (internet.LossPercent > 0) notes.Add($"فقد حزم {internet.LossPercent:0.#}%");
        if (internet.JitterMs is > 8) notes.Add($"تذبذب مرتفع {internet.JitterMs:0.0} ms");
        if (gatewayStats?.AverageMs is > 5) notes.Add($"زمن البوابة مرتفع {gatewayStats.AverageMs:0.0} ms");
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

    private sealed record PingStats(string Host, int SuccessCount, double? AverageMs, double? JitterMs, double LossPercent);
}
