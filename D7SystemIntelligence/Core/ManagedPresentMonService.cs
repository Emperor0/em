using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record PresentMonBackendInfo(bool Available,string? ExecutablePath,string Detail);
public sealed record FrameMetricsSnapshot(int ProcessId,string ProcessName,int Samples,double Fps,double OnePercentLow,double P95FrameMs,double P99FrameMs,double MaxFrameMs,DateTime UpdatedUtc);

public sealed class ManagedPresentMonService
{
    private const string LatestApi="https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";
    private static readonly HttpClient Http=CreateClient();
    private readonly string _root;

    public ManagedPresentMonService()
    {
        _root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"D7SystemIntelligence","Tools","PresentMon");
        Directory.CreateDirectory(_root);
    }

    public PresentMonBackendInfo Detect()
    {
        var exe=Directory.Exists(_root)?Directory.EnumerateFiles(_root,"PresentMon-*-x64.exe",SearchOption.AllDirectories).OrderByDescending(x=>x).FirstOrDefault():null;
        return exe==null?new(false,null,"PresentMon غير مجهز. D7 يستطيع تنزيل x64 الرسمي والتحقق من SHA-256."):new(true,exe,$"PresentMon جاهز: {exe}");
    }

    public async Task<PresentMonBackendInfo> EnsureAsync(IProgress<double>? progress=null,CancellationToken cancellationToken=default)
    {
        var current=Detect(); if(current.Available)return current;
        using var response=await Http.GetAsync(LatestApi,cancellationToken); response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
        var root=doc.RootElement;
        var tag=root.TryGetProperty("tag_name",out var t)?t.GetString()??"latest":"latest";
        string? url=null,digest=null,name=null;
        if(root.TryGetProperty("assets",out var assets)&&assets.ValueKind==JsonValueKind.Array)
        {
            foreach(var a in assets.EnumerateArray())
            {
                var n=a.GetProperty("name").GetString()??string.Empty;
                if(!n.StartsWith("PresentMon-",StringComparison.OrdinalIgnoreCase)||!n.EndsWith("-x64.exe",StringComparison.OrdinalIgnoreCase))continue;
                name=n;url=a.GetProperty("browser_download_url").GetString();
                if(a.TryGetProperty("digest",out var d)&&d.ValueKind==JsonValueKind.String)digest=d.GetString();
                break;
            }
        }
        if(string.IsNullOrWhiteSpace(url)||string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("لم يجد D7 PresentMon x64 في الإصدار الرسمي.");
        if(string.IsNullOrWhiteSpace(digest)||!digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("PresentMon release لا يعرض SHA-256؛ D7 رفض تشغيل ملف غير متحقق.");

        var folder=Path.Combine(_root,Sanitize(tag));Directory.CreateDirectory(folder);var path=Path.Combine(folder,name);
        using(var download=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellationToken))
        {
            download.EnsureSuccessStatusCode();var total=download.Content.Headers.ContentLength;
            await using var input=await download.Content.ReadAsStreamAsync(cancellationToken);await using var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,128*1024,true);
            var buffer=new byte[128*1024];long received=0;
            while(true){var read=await input.ReadAsync(buffer,cancellationToken);if(read<=0)break;await output.WriteAsync(buffer.AsMemory(0,read),cancellationToken);received+=read;if(total is>0)progress?.Report(received*100d/total.Value);}
        }
        var expected=digest[7..].Trim();var actual=await Sha256Async(path,cancellationToken);
        if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase)){try{File.Delete(path);}catch{}throw new InvalidOperationException("SHA-256 لـPresentMon غير مطابق؛ تم حذف الملف.");}
        progress?.Report(100);return new(true,path,$"تم تجهيز PresentMon {tag} الرسمي والتحقق من SHA-256.");
    }

    public FrameMetricsMonitor CreateMonitor()=>new(this);

    internal async Task<string> GetExecutableAsync(CancellationToken cancellationToken)
    {
        var info=await EnsureAsync(null,cancellationToken);return info.ExecutablePath??throw new InvalidOperationException(info.Detail);
    }

    private static async Task<string> Sha256Async(string path,CancellationToken token){await using var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,true);using var sha=SHA256.Create();return Convert.ToHexString(await sha.ComputeHashAsync(s,token)).ToLowerInvariant();}
    private static string Sanitize(string v)=>string.Concat(v.Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch));
    private static HttpClient CreateClient(){var c=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};c.DefaultRequestHeaders.UserAgent.ParseAdd("D7SystemIntelligence-PresentMon/1.0");c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");return c;}
}

public sealed class FrameMetricsMonitor:IAsyncDisposable
{
    private readonly ManagedPresentMonService _backend;
    private readonly object _gate=new();
    private readonly Queue<double> _frames=new();
    private CancellationTokenSource? _cts;
    private Process? _process;
    private int _pid;
    private string _processName=string.Empty;
    private DateTime _updated;

    internal FrameMetricsMonitor(ManagedPresentMonService backend)=>_backend=backend;
    public bool IsRunning=>_process is{HasExited:false};

    public async Task StartAsync(int processId,CancellationToken cancellationToken=default)
    {
        await StopAsync();
        using var target=Process.GetProcessById(processId);
        _pid=processId;_processName=target.ProcessName;
        var exe=await _backend.GetExecutableAsync(cancellationToken);
        _cts=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock(_gate)_frames.Clear();
        _process=new Process{StartInfo=new ProcessStartInfo{FileName=exe,Arguments=$"--process_id {processId} --output_stdout --no_console_stats --session_name D7_{processId} --stop_existing_session",UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};
        _process.Start();
        _=Task.Run(()=>ReadLoopAsync(_process,_cts.Token));
        await Task.Delay(250,cancellationToken);
    }

    public FrameMetricsSnapshot? Read()
    {
        double[] data;lock(_gate)data=_frames.ToArray();
        if(data.Length<5)return null;
        var sorted=data.OrderBy(x=>x).ToArray();var avg=data.Average();
        var p95=Percentile(sorted,.95);var p99=Percentile(sorted,.99);var fps=avg>0?1000d/avg:0;var low=p99>0?1000d/p99:0;
        return new(_pid,_processName,data.Length,fps,low,p95,p99,sorted[^1],_updated);
    }

    public async Task StopAsync()
    {
        var cts=_cts;_cts=null;if(cts!=null){try{cts.Cancel();}catch{}}
        var p=_process;_process=null;
        if(p!=null){try{if(!p.HasExited){p.Kill(true);await p.WaitForExitAsync();}}catch{}p.Dispose();}
        cts?.Dispose();
    }

    private async Task ReadLoopAsync(Process p,CancellationToken token)
    {
        Dictionary<string,int>? columns=null;
        try
        {
            while(!token.IsCancellationRequested&&!p.HasExited)
            {
                var line=await p.StandardOutput.ReadLineAsync(token);if(line==null)break;if(string.IsNullOrWhiteSpace(line))continue;
                var cells=ParseCsv(line);
                if(columns==null)
                {
                    var idx=cells.FindIndex(x=>x.Equals("MsBetweenPresents",StringComparison.OrdinalIgnoreCase));
                    if(idx>=0){columns=cells.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i,StringComparer.OrdinalIgnoreCase);}continue;
                }
                if(!columns.TryGetValue("MsBetweenPresents",out var frameIdx)||frameIdx>=cells.Count)continue;
                if(!double.TryParse(cells[frameIdx],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var ms)||ms<=0||ms>1000)continue;
                lock(_gate){_frames.Enqueue(ms);while(_frames.Count>1200)_frames.Dequeue();_updated=DateTime.UtcNow;}
            }
        }
        catch(OperationCanceledException){}
        catch{}
    }

    private static List<string> ParseCsv(string line)
    {
        var list=new List<string>();var sb=new System.Text.StringBuilder();var quoted=false;
        for(var i=0;i<line.Length;i++)
        {
            var ch=line[i];if(ch=='\"'){if(quoted&&i+1<line.Length&&line[i+1]=='\"'){sb.Append('\"');i++;}else quoted=!quoted;}
            else if(ch==','&&!quoted){list.Add(sb.ToString());sb.Clear();}else sb.Append(ch);
        }
        list.Add(sb.ToString());return list;
    }
    private static double Percentile(double[] sorted,double p){var idx=Math.Clamp((int)Math.Ceiling(p*sorted.Length)-1,0,sorted.Length-1);return sorted[idx];}
    public async ValueTask DisposeAsync()=>await StopAsync();
}
