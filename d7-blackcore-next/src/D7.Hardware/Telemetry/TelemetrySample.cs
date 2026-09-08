namespace D7.Hardware.Telemetry;

public sealed record NvidiaTelemetry(
    bool Available,
    string? Name,
    double? UtilizationPercent,
    double? TemperatureC,
    double? PowerWatts,
    double? GraphicsClockMhz,
    double? MemoryUsedMiB,
    double? MemoryTotalMiB,
    string? DriverVersion,
    string? Error = null);

public sealed record SystemTelemetrySample(
    DateTimeOffset CapturedAt,
    double? CpuUtilizationPercent,
    double MemoryUsedPercent,
    double MemoryAvailableGiB,
    NvidiaTelemetry Nvidia,
    double D7CpuTimeSeconds,
    long D7WorkingSetBytes,
    long D7PrivateBytes,
    int D7ThreadCount);
