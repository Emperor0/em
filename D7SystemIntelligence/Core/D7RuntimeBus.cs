namespace D7SystemIntelligence.Core;

public static class D7RuntimeBus
{
    private static readonly object Gate = new();
    private static RuntimeContext? _context;
    private static D7Mission _mission;

    public static RuntimeContext? Context
    {
        get { lock (Gate) return _context; }
    }

    public static D7Mission Mission
    {
        get { lock (Gate) return _mission; }
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
}
