namespace D7SystemIntelligence;

public sealed record HardwareSnapshot(
    string CpuName, string GpuName, float CpuLoad, float GpuLoad,
    float CpuTemp, float GpuTemp, float RamLoad, float? VramLoad,
    IReadOnlyList<FanSnapshot> Fans);

public sealed record FanSnapshot(string Id, string Name, float? Rpm, bool Controllable, float? ControlPercent);
public sealed record GameRecord(string Name, string Launcher, string InstallPath, string? ExecutablePath, string Source);
public sealed record DiagnosticFinding(string Severity, string Area, string Title, string Detail, string? Recommendation = null);
public sealed record CodConfigResult(bool Found, string? Path, IReadOnlyDictionary<string,string> CurrentValues, string Message);
