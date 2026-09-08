namespace D7.Hardware.Models;

public enum TriState
{
    Unknown,
    Disabled,
    Enabled
}

public sealed record CpuInfo(
    string Name,
    int LogicalProcessors,
    string Architecture);

public sealed record MemoryInfo(
    ulong TotalBytes,
    ulong AvailableBytes)
{
    public double TotalGiB => Math.Round(TotalBytes / 1073741824d, 2);
    public double AvailableGiB => Math.Round(AvailableBytes / 1073741824d, 2);
    public double UsedPercent => TotalBytes == 0 ? 0 : Math.Round((1d - AvailableBytes / (double)TotalBytes) * 100d, 1);
}

public sealed record BoardInfo(
    string Manufacturer,
    string Product,
    string BiosVersion,
    string BiosDate);

public sealed record WindowsInfo(
    string ProductName,
    string DisplayVersion,
    string Build,
    int Ubr,
    TriState GameMode,
    TriState Hags,
    TriState GameDvr,
    TriState VbsConfigured,
    TriState MemoryIntegrityConfigured,
    TriState SecureBoot,
    IReadOnlyList<string> PagefileEntries);

public sealed record DisplayAdapterInfo(
    string Name,
    string DeviceName,
    string DeviceId,
    bool AttachedToDesktop,
    bool Primary,
    bool MirroringDriver);

public sealed record StorageInfo(
    string Name,
    string RootPath,
    string DriveType,
    string FileSystem,
    long TotalBytes,
    long FreeBytes)
{
    public double TotalGiB => Math.Round(TotalBytes / 1073741824d, 1);
    public double FreeGiB => Math.Round(FreeBytes / 1073741824d, 1);
}

public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    string InterfaceType,
    string Status,
    long SpeedBitsPerSecond,
    bool SupportsIpv4,
    bool SupportsIpv6);

public sealed record StartupEntry(
    string Scope,
    string Name,
    string Command);

public sealed record HardwareSnapshot(
    DateTimeOffset CapturedAt,
    CpuInfo Cpu,
    MemoryInfo Memory,
    BoardInfo Board,
    WindowsInfo Windows,
    IReadOnlyList<DisplayAdapterInfo> DisplayAdapters,
    IReadOnlyList<StorageInfo> Storage,
    IReadOnlyList<NetworkAdapterInfo> NetworkAdapters,
    IReadOnlyList<StartupEntry> StartupEntries);
