using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed class LauncherScanner
{
    public Task<List<GameRecord>> ScanAsync() => Task.Run(Scan);

    private List<GameRecord> Scan()
    {
        var games = new List<GameRecord>();
        TrySteam(games); TryEpic(games); TryXbox(games); TryUbisoft(games); TryRegistryPublishers(games);
        return games.GroupBy(g => $"{g.Launcher}|{g.InstallPath}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(g => g.Launcher).ThenBy(g => g.Name).ToList();
    }

    private static void TrySteam(List<GameRecord> games)
    {
        string? steam = ReadRegString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(steam) || !Directory.Exists(steam)) return;
        var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            var text = File.ReadAllText(vdf);
            foreach (Match m in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var lib in libs)
        {
            var apps = Path.Combine(lib, "steamapps"); if (!Directory.Exists(apps)) continue;
            foreach (var mf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string t; try { t = File.ReadAllText(mf); } catch { continue; }
                var name = MatchVdf(t, "name") ?? Path.GetFileNameWithoutExtension(mf);
                var dir = MatchVdf(t, "installdir"); if (string.IsNullOrWhiteSpace(dir)) continue;
                var install = Path.Combine(apps, "common", dir);
                games.Add(new GameRecord(name, "Steam", install, FindLikelyExe(install), mf));
            }
        }
    }

    private static string? MatchVdf(string t, string key)
    {
        var m = Regex.Match(t, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value : null;
    }

    private static void TryEpic(List<GameRecord> games)
    {
        var dat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (!File.Exists(dat)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(dat));
            if (doc.RootElement.TryGetProperty("InstallationList", out var list))
            foreach (var e in list.EnumerateArray())
            {
                var path = e.TryGetProperty("InstallLocation", out var p) ? p.GetString() : null;
                var name = e.TryGetProperty("AppName", out var n) ? n.GetString() : null;
                if (!string.IsNullOrWhiteSpace(path)) games.Add(new GameRecord(name ?? Path.GetFileName(path), "Epic", path!, FindLikelyExe(path!), dat));
            }
        } catch { }
    }

    private static void TryXbox(List<GameRecord> games)
    {
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
        {
            var root = Path.Combine(d.RootDirectory.FullName, "XboxGames"); if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var content = Path.Combine(dir, "Content"); var install = Directory.Exists(content) ? content : dir;
                    games.Add(new GameRecord(Path.GetFileName(dir), "Xbox", install, FindLikelyExe(install), root));
                }
            } catch { }
        }
    }

    private static void TryUbisoft(List<GameRecord> games)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", @"SOFTWARE\Ubisoft\Launcher\Installs" })
        {
            try
            {
                using var key = hive.OpenSubKey(keyPath); if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                using (var k = key.OpenSubKey(sub))
                {
                    var path = k?.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) games.Add(new GameRecord(Path.GetFileName(path.TrimEnd('\\')), "Ubisoft", path, FindLikelyExe(path), $"Registry:{keyPath}\\{sub}"));
                }
            } catch { }
        }
    }

    private static void TryRegistryPublishers(List<GameRecord> games)
    {
        var roots = new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var root in roots)
        {
            try
            {
                using var k = hive.OpenSubKey(root); if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                using (var s = k.OpenSubKey(sub))
                {
                    var name = s?.GetValue("DisplayName") as string; var pub = s?.GetValue("Publisher") as string; var path = s?.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
                    var hay = $"{name} {pub}";
                    string? launcher = hay.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) || hay.Contains("EA", StringComparison.OrdinalIgnoreCase) ? "EA App" :
                        hay.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) || hay.Contains("Activision", StringComparison.OrdinalIgnoreCase) ? "Battle.net" :
                        hay.Contains("Rockstar", StringComparison.OrdinalIgnoreCase) ? "Rockstar" : hay.Contains("GOG", StringComparison.OrdinalIgnoreCase) ? "GOG" : hay.Contains("Amazon", StringComparison.OrdinalIgnoreCase) ? "Amazon Games" : null;
                    if (launcher != null) games.Add(new GameRecord(name, launcher, path, FindLikelyExe(path), $"Registry:{root}\\{sub}"));
                }
            } catch { }
        }
    }

    private static string? FindLikelyExe(string root)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !Regex.IsMatch(Path.GetFileName(p), "(unins|uninstall|crash|report|launcher|setup|update|redistributable)", RegexOptions.IgnoreCase))
                .OrderByDescending(p => new FileInfo(p).Length).FirstOrDefault();
        } catch { return null; }
    }

    private static string? ReadRegString(RegistryKey hive, string path, string name)
    { try { using var k = hive.OpenSubKey(path); return k?.GetValue(name) as string; } catch { return null; } }
}
