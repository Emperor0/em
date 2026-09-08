using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;
using D7.Hardware.Models;
using D7.Hardware.Native;

namespace D7.Hardware.Discovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareDiscoveryService
{
    public Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    public HardwareSnapshot Capture(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cpu = new CpuInfo(
            ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "غير معروف",
            Environment.ProcessorCount,
            RuntimeInformation.ProcessArchitecture.ToString());

        var memory = ReadMemory();
        var board = ReadBoard();
        var windows = ReadWindows();

        cancellationToken.ThrowIfCancellationRequested();
        var displays = ReadDisplayAdapters();
        var storage = ReadStorage();
        var network = ReadNetworkAdapters();
        var startup = ReadStartupEntries();

        return new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            cpu,
            memory,
            board,
            windows,
            displays,
            storage,
            network,
            startup);
    }

    public static TriState DecodeBooleanRegistryValue(object? value)
    {
        if (value is null) return TriState.Unknown;
        try
        {
            var number = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return number == 0 ? TriState.Disabled : TriState.Enabled;
        }
        catch
        {
            return TriState.Unknown;
        }
    }

    public static TriState DecodeHagsRegistryValue(object? value)
    {
        if (value is null) return TriState.Unknown;
        try
        {
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) switch
            {
                1 => TriState.Disabled,
                2 => TriState.Enabled,
                _ => TriState.Unknown
            };
        }
        catch
        {
            return TriState.Unknown;
        }
    }

    private static MemoryInfo ReadMemory()
    {
        var status = new NativeMethods.MemoryStatusEx();
        return NativeMethods.GlobalMemoryStatusEx(status)
            ? new MemoryInfo(status.ullTotalPhys, status.ullAvailPhys)
            : new MemoryInfo(0, 0);
    }

    private static BoardInfo ReadBoard() => new(
        ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer")
            ?? ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer")
            ?? "غير معروف",
        ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct")
            ?? ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName")
            ?? "غير معروف",
        ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion") ?? "غير معروف",
        ReadString(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSReleaseDate") ?? "غير معروف");

    private static WindowsInfo ReadWindows()
    {
        const string currentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var productName = ReadString(RegistryHive.LocalMachine, currentVersion, "ProductName") ?? "Windows";
        var displayVersion = ReadString(RegistryHive.LocalMachine, currentVersion, "DisplayVersion")
            ?? ReadString(RegistryHive.LocalMachine, currentVersion, "ReleaseId")
            ?? string.Empty;
        var build = ReadString(RegistryHive.LocalMachine, currentVersion, "CurrentBuildNumber")
            ?? Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ubr = ReadInt(RegistryHive.LocalMachine, currentVersion, "UBR") ?? 0;

        var gameModeRaw = ReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled")
            ?? ReadValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode");
        var hagsRaw = ReadValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        var gameDvrRaw = ReadValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled")
            ?? ReadValue(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
        var vbsRaw = ReadValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity");
        var hvciRaw = ReadValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        var secureBootRaw = ReadValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        var pagefiles = ReadMultiString(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "PagingFiles");

        return new WindowsInfo(
            productName,
            displayVersion,
            build,
            ubr,
            DecodeBooleanRegistryValue(gameModeRaw),
            DecodeHagsRegistryValue(hagsRaw),
            DecodeBooleanRegistryValue(gameDvrRaw),
            DecodeBooleanRegistryValue(vbsRaw),
            DecodeBooleanRegistryValue(hvciRaw),
            DecodeBooleanRegistryValue(secureBootRaw),
            pagefiles);
    }

    private static IReadOnlyList<DisplayAdapterInfo> ReadDisplayAdapters()
    {
        var list = new List<DisplayAdapterInfo>();
        for (uint index = 0; index < 32; index++)
        {
            var device = new NativeMethods.DisplayDevice { cb = Marshal.SizeOf<NativeMethods.DisplayDevice>() };
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0)) break;

            var attached = (device.StateFlags & NativeMethods.DisplayDeviceAttachedToDesktop) != 0;
            var primary = (device.StateFlags & NativeMethods.DisplayDevicePrimaryDevice) != 0;
            var mirror = (device.StateFlags & NativeMethods.DisplayDeviceMirroringDriver) != 0;

            list.Add(new DisplayAdapterInfo(
                string.IsNullOrWhiteSpace(device.DeviceString) ? "غير معروف" : device.DeviceString,
                device.DeviceName ?? string.Empty,
                device.DeviceId ?? string.Empty,
                attached,
                primary,
                mirror));
        }
        return list;
    }

    private static IReadOnlyList<StorageInfo> ReadStorage()
    {
        var list = new List<StorageInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                list.Add(new StorageInfo(
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel,
                    drive.RootDirectory.FullName,
                    drive.DriveType.ToString(),
                    drive.DriveFormat,
                    drive.TotalSize,
                    drive.AvailableFreeSpace));
            }
            catch (IOException)
            {
                // Removable/network drives can disappear during enumeration.
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible volume without breaking the full scan.
            }
        }
        return list;
    }

    private static IReadOnlyList<NetworkAdapterInfo> ReadNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var properties = adapter.GetIPProperties();
                list.Add(new NetworkAdapterInfo(
                    adapter.Name,
                    adapter.Description,
                    adapter.NetworkInterfaceType.ToString(),
                    adapter.OperationalStatus.ToString(),
                    adapter.Speed,
                    properties.GetIPv4Properties() is not null,
                    properties.GetIPv6Properties() is not null));
            }
            catch (NetworkInformationException)
            {
                // Adapter may be removed while enumerating.
            }
        }
        return list;
    }

    private static IReadOnlyList<StartupEntry> ReadStartupEntries()
    {
        var list = new List<StartupEntry>();
        AddRunKey(list, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "المستخدم");
        AddRunKey(list, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "المستخدم - مرة واحدة");
        AddRunKey(list, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "النظام");
        AddRunKey(list, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "النظام - مرة واحدة");
        return list;
    }

    private static void AddRunKey(List<StartupEntry> list, RegistryHive hive, string path, string scope)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name)?.ToString();
                if (!string.IsNullOrWhiteSpace(command)) list.Add(new StartupEntry(scope, name, command));
            }
        }
        catch (Exception ex) when (ex is Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Startup inventory is best effort and never a reason to fail the full scan.
        }
    }

    private static string? ReadString(RegistryHive hive, string path, string name) => ReadValue(hive, path, name)?.ToString()?.Trim();

    private static int? ReadInt(RegistryHive hive, string path, string name)
    {
        var value = ReadValue(hive, path, name);
        if (value is null) return null;
        try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static IReadOnlyList<string> ReadMultiString(RegistryHive hive, string path, string name)
    {
        var value = ReadValue(hive, path, name);
        return value switch
        {
            string[] values => values.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            string single when !string.IsNullOrWhiteSpace(single) => [single],
            _ => Array.Empty<string>()
        };
    }

    private static object? ReadValue(RegistryHive hive, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: false);
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex) when (ex is Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
