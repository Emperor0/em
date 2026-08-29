using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class StreamDirectorWindow:Window,IAsyncDisposable
{
    private readonly StreamDirectorService _service=new();
    private readonly StreamProcessGovernor _governor=new();
    private readonly string? _game;
    private readonly TextBlock _status=new();
    private readonly TextBlock _obs=new();
    private readonly TextBlock _frames=new();
    private readonly TextBlock _outputs=new();
    private readonly DispatcherTimer _timer;
    private bool _busy;

    public StreamDirectorWindow(string? gameProcessName)
    {
        _game=gameProcessName;
        Title="D7 — Stream Director";Width=820;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FlowDirection=FlowDirection.RightToLeft;Background=Brush("Bg",Brushes.Black);Foreground=Brush("Text",Brushes.White);
        var root=new StackPanel{Margin=new Thickness(20)};
        root.Children.Add(new TextBlock{Text="Stream Director",FontSize=28,FontWeight=FontWeights.SemiBold});
        root.Children.Add(new TextBlock{Text="مباشر من OBS WebSocket: CPU/Memory/FPS/Render Time/Skipped Frames/Congestion + حالة Stream/Record/Virtual Camera وTikTok. Governor يحفظ Priority الحالية ويرجعها عند الإيقاف.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("Muted",Brushes.Gray),Margin=new Thickness(0,6,0,14)});
        root.Children.Add(Card("OBS / TikTok",_obs));root.Children.Add(Card("Render / Output Health",_frames));root.Children.Add(Card("Outputs",_outputs));
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        var apply=new Button{Content="تشغيل Stream Governor"};apply.Click+=(_,_)=>_status.Text=_governor.Apply(_game);
        var restore=new Button{Content="إيقاف Governor + استعادة"};restore.Click+=(_,_)=>_status.Text=_governor.Restore();
        row.Children.Add(apply);row.Children.Add(restore);root.Children.Add(row);
        _status.TextWrapping=TextWrapping.Wrap;_status.Margin=new Thickness(0,10,0,0);root.Children.Add(_status);
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        _timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};_timer.Tick+=async(_,_)=>await RefreshAsync();
        Loaded+=async(_,_)=>{await RefreshAsync();_timer.Start();};
        Closed+=async(_,_)=>await DisposeAsync();
    }

    private async Task RefreshAsync()
    {
        if(_busy)return;_busy=true;
        try
        {
            var s=await _service.ReadAsync();
            _obs.Text=$"OBS: {(s.ObsRunning?"شغال":"متوقف")} | WebSocket: {(s.Connected?"متصل":"غير متصل")} | TikTok: {(s.TikTokRunning?"شغال":"متوقف")}\nOBS CPU {s.ObsCpuUsage:0.0}% | RAM {s.ObsMemoryMb:0} MB | Active FPS {s.ActiveFps:0.0}";
            var renderDrop=s.RenderTotalFrames>0?s.RenderSkippedFrames*100d/s.RenderTotalFrames:0;var outputDrop=s.OutputTotalFrames>0?s.OutputSkippedFrames*100d/s.OutputTotalFrames:0;
            _frames.Text=$"Average Frame Render {s.AverageFrameRenderMs:0.00} ms | Render skipped {s.RenderSkippedFrames:N0}/{s.RenderTotalFrames:N0} ({renderDrop:0.000}%)\nOutput skipped {s.OutputSkippedFrames:N0}/{s.OutputTotalFrames:N0} ({outputDrop:0.000}%) | Congestion {s.OutputCongestion*100:0.0}%";
            _outputs.Text=$"Stream {(s.Streaming?"ON":"OFF")} | Recording {(s.Recording?"ON":"OFF")} | Virtual Camera {(s.VirtualCamera?"ON":"OFF")}\n{s.Detail}";
            _status.Text=BuildVerdict(s,renderDrop,outputDrop);
        }
        catch(Exception ex){_status.Text=ex.Message;}finally{_busy=false;}
    }

    private static string BuildVerdict(StreamDirectorSnapshot s,double renderDrop,double outputDrop)
    {
        if(!s.Connected)return s.Detail;
        var warnings=new List<string>();
        if(s.AverageFrameRenderMs>12)warnings.Add("OBS render time مرتفع");
        if(renderDrop>.1)warnings.Add($"Render skipped {renderDrop:0.00}%");
        if(outputDrop>.1)warnings.Add($"Output skipped {outputDrop:0.00}%");
        if(s.OutputCongestion>.05)warnings.Add($"Network congestion {s.OutputCongestion*100:0.0}%");
        return warnings.Count==0?"حالة OBS الحالية مستقرة حسب القياسات المتاحة.":"تنبيه: "+string.Join(" • ",warnings);
    }

    private static Border Card(string title,TextBlock content)
    {
        content.Text="…";content.TextWrapping=TextWrapping.Wrap;content.Margin=new Thickness(0,6,0,0);
        return new Border{Background=Brush("Panel",Brushes.DimGray),CornerRadius=new CornerRadius(12),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),Child=new StackPanel{Children={new TextBlock{Text=title,FontSize=19,FontWeight=FontWeights.SemiBold},content}}};
    }
    private static Brush Brush(string key,Brush fallback)=>Application.Current.TryFindResource(key) as Brush??fallback;
    public async ValueTask DisposeAsync(){_timer.Stop();_governor.Dispose();await _service.DisposeAsync();}
}
