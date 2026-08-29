namespace D7SystemIntelligence.Core;

public sealed class SmartFanController : IDisposable
{
    private readonly HardwareEngine _hardware;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private float? _lastPercent;

    public event Action<string>? StatusChanged;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public SmartFanController(HardwareEngine hardware) => _hardware = hardware;

    public bool Start()
    {
        if (IsRunning) return true;
        var snapshot = _hardware.Read();
        var count = snapshot.Fans.Count(x => x.Controllable);
        if (count == 0)
        {
            StatusChanged?.Invoke("AUTO غير متاح: لم يثبت وجود أي قناة مراوح قابلة للكتابة على هذا الجهاز.");
            return false;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        StatusChanged?.Invoke($"AUTO Fan بدأ على {count} قناة قابلة للكتابة. الاستعادة إلى BIOS/AUTO تتم عند الإيقاف.");
        return true;
    }

    public void Stop(bool restore = true)
    {
        var cts = _cts;
        if (cts == null) return;
        try { cts.Cancel(); } catch { }
        _cts = null;
        _lastPercent = null;
        if (restore)
        {
            try { _hardware.RestoreFans(); } catch { }
            StatusChanged?.Invoke("تم إيقاف AUTO Fan واستعادة تحكم BIOS/Default للقنوات المدعومة.");
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var s = _hardware.Read();
                var temp = Math.Max(s.CpuTemp, s.GpuTemp);
                var target = Curve(temp);

                // Hysteresis: avoid constant PWM writes for tiny temperature changes.
                if (!_lastPercent.HasValue || Math.Abs(target - _lastPercent.Value) >= 4f || target >= 100f)
                {
                    var changed = 0;
                    foreach (var fan in s.Fans.Where(x => x.Controllable))
                    {
                        if (_hardware.SetFanControl(fan.Id, target, out _)) changed++;
                    }
                    if (changed > 0)
                    {
                        _lastPercent = target;
                        StatusChanged?.Invoke($"AUTO Fan • حرارة مرجعية {temp:0}°C • هدف {target:0}% • {changed} قناة");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("AUTO Fan توقف بسبب خطأ: " + ex.Message);
        }
        finally
        {
            try { _hardware.RestoreFans(); } catch { }
        }
    }

    private static float Curve(float temp)
    {
        if (temp >= 88) return 100;
        if (temp <= 42) return 32;
        if (temp <= 55) return Lerp(42, 55, 32, 45, temp);
        if (temp <= 65) return Lerp(55, 65, 45, 60, temp);
        if (temp <= 75) return Lerp(65, 75, 60, 76, temp);
        if (temp <= 82) return Lerp(75, 82, 76, 90, temp);
        return Lerp(82, 88, 90, 100, temp);
    }

    private static float Lerp(float x1, float x2, float y1, float y2, float x)
        => Math.Clamp(y1 + (x - x1) / (x2 - x1) * (y2 - y1), Math.Min(y1, y2), Math.Max(y1, y2));

    public void Dispose() => Stop(true);
}
