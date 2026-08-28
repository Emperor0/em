namespace D7SystemIntelligence;

public sealed record HardwareSnapshot(
    string CpuName, string GpuName, float CpuLoad, float GpuLoad,
    float CpuTemp, float GpuTemp, float RamLoad, float? VramLoad,
    IReadOnlyList<FanSnapshot> Fans);

public sealed record FanSnapshot(string Id, string Name, float? Rpm, bool Controllable, float? ControlPercent);
public sealed record GameRecord(string Name, string Launcher, string InstallPath, string? ExecutablePath, string Source);
public sealed record DiagnosticFinding(string Severity, string Area, string Title, string Detail, string? Recommendation = null);
public sealed record CodConfigResult(bool Found, string? Path, IReadOnlyDictionary<string,string> CurrentValues, string Message);
public sealed record NetworkReport(string AdapterName, string IPv4, long LinkSpeedBps, double? InternetLatencyMs, double? JitterMs, double? GatewayLatencyMs, double PacketLossPercent, string Notes);
public sealed record PeripheralRecord(string Category, string Name, string Status, string InstanceId);
public sealed record DriverRecord(string DeviceName, string DeviceClass, string DriverVersion, string DriverDate, string Manufacturer, string InfName);
