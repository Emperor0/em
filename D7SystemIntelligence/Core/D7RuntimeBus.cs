namespace D7SystemIntelligence.Core;

public static class D7RuntimeBus
{
    private static readonly object Gate = new();
    private static RuntimeContext? _context;
    private static D7Mission _mission;
    private static HardwareSnapshot? _hardware;
    private static GameSessionSample? _sessionSample;

    public static RuntimeContext? Context
    {
        get { lock (Gate) return _context; }
    }

    public static D7Mission Mission
    {
        get { lock (Gate) return _mission; }
    }

    public static HardwareSnapshot? Hardware
    {
        get { lock (Gate) return _hardware; }
    }

    public static GameSessionSample? SessionSample
    {
        get { lock (Gate) return _sessionSample; }
    }

    public static event Action? Changed;

    public static void PublishContext(RuntimeContext context)
    {
        lock (Gate) _context = context;
        Changed?.Invoke();
    }

    public static void PublishMission(D7Mission mission)
    {
        lock (Gate) _mission = mission;
        Changed?.Invoke();
    }

    public static void PublishHardware(HardwareSnapshot snapshot)
    {
        lock (Gate) _hardware = snapshot;
        Changed?.Invoke();
    }

    public static void PublishSessionSample(GameSessionSample sample)
    {
        lock (Gate) _sessionSample = sample;
        Changed?.Invoke();
    }

    public static void ClearSessionSample()
    {
        lock (Gate) _sessionSample = null;
        Changed?.Invoke();
    }
}