using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record StreamDirectorSnapshot(
    bool ObsRunning,bool Connected,bool TikTokRunning,bool Streaming,bool Recording,bool VirtualCamera,
    double ObsCpuUsage,double ObsMemoryMb,double ActiveFps,double AverageFrameRenderMs,
    long RenderSkippedFrames,long RenderTotalFrames,long OutputSkippedFrames,long OutputTotalFrames,double OutputCongestion,
    string Detail);

public sealed class StreamDirectorService:IAsyncDisposable
{
    private ObsWebSocketClient? _obs;

    public async Task<StreamDirectorSnapshot> ReadAsync(CancellationToken token=default)
    {
        var obsRunning=Process.GetProcessesByName("obs64").Any()||Process.GetProcessesByName("obs").Any();
        var tiktok=Process.GetProcesses().Any(p=>SafeName(p).Contains("tiktok",StringComparison.OrdinalIgnoreCase));
        if(!obsRunning)return new(false,false,tiktok,false,false,false,0,0,0,0,0,0,0,0,0,"OBS غير شغال.");
        try
        {
            await EnsureConnectedAsync(token);
            var stats=await _obs!.RequestAsync("GetStats",cancellationToken:token);
            var stream=await SafeRequestAsync("GetStreamStatus",token);
            var record=await SafeRequestAsync("GetRecordStatus",token);
            var cam=await SafeRequestAsync("GetVirtualCamStatus",token);
            return new StreamDirectorSnapshot(
                true,true,tiktok,B(stream,"outputActive"),B(record,"outputActive"),B(cam,"outputActive"),
                D(stats,"cpuUsage"),D(stats,"memoryUsage"),D(stats,"activeFps"),D(stats,"averageFrameRenderTime"),
                L(stats,"renderSkippedFrames"),L(stats,"renderTotalFrames"),L(stats,"outputSkippedFrames"),L(stats,"outputTotalFrames"),D(stream,"outputCongestion"),
                "OBS WebSocket متصل والبيانات مباشرة من OBS.");
        }
        catch(Exception ex)
        {
            return new(true,false,tiktok,false,false,false,0,0,0,0,0,0,0,0,0,"تعذر الاتصال بـOBS WebSocket: "+ex.Message);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken token)
    {
        if(_obs?.IsConnected==true)return;
        if(_obs!=null)await _obs.DisposeAsync();
        _obs=new ObsWebSocketClient();
        var password=WindowsCredentialStore.Read(ShadowCaptureService.ObsCredentialTarget);
        await _obs.ConnectAsync("127.0.0.1",4455,password,token);
    }

    private async Task<JsonElement> SafeRequestAsync(string type,CancellationToken token)
    {
        try{return await _obs!.RequestAsync(type,cancellationToken:token);}catch{using var d=JsonDocument.Parse("{}");return d.RootElement.Clone();}
    }
    private static bool B(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.True;
    private static double D(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetDouble(out var v)?v:0;
    private static long L(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetInt64(out var v)?v:0;
    private static string SafeName(Process p){try{return p.ProcessName;}catch{return string.Empty;}finally{p.Dispose();}}
    public async ValueTask DisposeAsync(){if(_obs!=null)await _obs.DisposeAsync();_obs=null;}
}

public sealed class StreamProcessGovernor:IDisposable
{
    private readonly Dictionary<int,ProcessPriorityClass> _original=new();
    public bool Active=>_original.Count>0;
    public int ChangedCount=>_original.Count;

    public string Apply(string? gameProcessName)
    {
        Restore();
        var changed=new List<string>();
        var already=new List<string>();
        if(!string.IsNullOrWhiteSpace(gameProcessName))SetByName(gameProcessName,ProcessPriorityClass.AboveNormal,changed,already);
        SetByName("obs64",ProcessPriorityClass.AboveNormal,changed,already);
        SetByName("obs",ProcessPriorityClass.AboveNormal,changed,already);
        foreach(var p in Process.GetProcesses())
        {
            string name;try{name=p.ProcessName;}catch{p.Dispose();continue;}
            if(name.Contains("tiktok",StringComparison.OrdinalIgnoreCase))SetProcess(p,ProcessPriorityClass.Normal,changed,already);else p.Dispose();
        }
        foreach(var bg in new[]{"chrome","msedge","Discord"})SetByName(bg,ProcessPriorityClass.BelowNormal,changed,already);

        if(changed.Count==0 && already.Count==0)return "Unsupported/Not Running • لم يجد D7KT عمليات مناسبة لـStream Governor.";
        if(changed.Count==0)return "Already Optimal • كل العمليات المكتشفة على Priority المطلوبة بالفعل.\n"+string.Join(Environment.NewLine,already);
        var lines=new List<string>{"Applied + Verified • تم تغيير Priority للعمليات التالية:"};
        lines.AddRange(changed);
        if(already.Count>0){lines.Add("Already Optimal:");lines.AddRange(already);}
        return string.Join(Environment.NewLine,lines);
    }

    public string Restore()
    {
        var count=0;
        foreach(var kv in _original.ToArray())
        {
            try{using var p=Process.GetProcessById(kv.Key);if(!p.HasExited){p.PriorityClass=kv.Value;if(p.PriorityClass==kv.Value)count++;}}catch{}
        }
        _original.Clear();return count>0?$"Restore Verified • تمت استعادة Priority لـ{count} عملية.":"لا توجد Priorities غيّرها D7KT تحتاج استعادة.";
    }

    private void SetByName(string name,ProcessPriorityClass priority,List<string> changed,List<string> already)
    {
        foreach(var p in Process.GetProcessesByName(name))SetProcess(p,priority,changed,already);
    }
    private void SetProcess(Process p,ProcessPriorityClass priority,List<string> changed,List<string> already)
    {
        using(p)
        {
            try
            {
                var before=p.PriorityClass;
                if(before==priority){already.Add($"{p.ProcessName}: {priority}");return;}
                p.PriorityClass=priority;
                var after=p.PriorityClass;
                if(after==priority)
                {
                    _original[p.Id]=before;
                    changed.Add($"{p.ProcessName}: {before} → {after}");
                }
            }
            catch{}
        }
    }
    public void Dispose()=>Restore();
}