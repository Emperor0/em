using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed class LauncherScanner
{
    private readonly GameIdentityStore _identities = new();

    public Task<List<GameRecord>> ScanAsync() => Task.Run(Scan);

    private List<GameRecord> Scan()
    {
        var games = new List<GameRecord>();
        TrySteam(games);
        TryEpicManifests(games);
        TryEpicInstalledData(games);
        TryXbox(games);
        TryUbisoft(games);
        TryRegistryPublishers(games);

        return games
            .Where(g => Directory.Exists(g.InstallPath))
            .GroupBy(g => $"{g.Launcher}|{Normalize(g.InstallPath)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => Best(g))
            .Select(_identities.Apply)
            .OrderBy(g => g.Launcher)
            .ThenBy(g => g.Name)
            .ToList();
    }

    private static GameRecord Best(IEnumerable<GameRecord> candidates)
        => candidates.OrderByDescending(x => SourceRank(x.Source))
            .ThenByDescending(x => x.ExecutablePath != null && File.Exists(x.ExecutablePath))
            .First();

    private static int SourceRank(string source)
    {
        if (source.Contains("EpicManifest", StringComparison.OrdinalIgnoreCase)) return 100;
        if (source.Contains("SteamManifest", StringComparison.OrdinalIgnoreCase)) return 90;
        if (source.Contains("XboxGames", StringComparison.OrdinalIgnoreCase)) return 80;
        if (source.Contains("UbisoftRegistry", StringComparison.OrdinalIgnoreCase)) return 70;
        if (source.Contains("LauncherInstalled", StringComparison.OrdinalIgnoreCase)) return 60;
        return 30;
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
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var mf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string t;
                try { t = File.ReadAllText(mf); } catch { continue; }
                var name = MatchVdf(t, "name") ?? Path.GetFileNameWithoutExtension(mf);
                var dir = MatchVdf(t, "installdir");
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var install = Path.Combine(apps, "common", dir);
                if (!Directory.Exists(install)) continue;
                var appId = Regex.Match(Path.GetFileNameWithoutExtension(mf), @"appmanifest_(\d+)", RegexOptions.IgnoreCase).Groups[1].Value;
                games.Add(new GameRecord(name, "Steam", install, FindLikelyExe(install), $"SteamManifest:{appId}:{mf}"));
            }
        }
    }

    private static string? MatchVdf(string t, string key)
    {
        var m = Regex.Match(t, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static void TryEpicManifests(List<GameRecord> games)
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        foreach (var file in Directory.EnumerateFiles(manifests, "*.item", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var install = J(root, "InstallLocation");
                if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install)) continue;
                var name = J(root, "DisplayName");
                if (string.IsNullOrWhiteSpace(name)) name = J(root, "AppName");
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(install.TrimEnd('\\'));
                var launch = J(root, "LaunchExecutable");
                string? exact = null;
                if (!string.IsNullOrWhiteSpace(launch))
                {
                    var candidate = Path.GetFullPath(Path.Combine(install, launch.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(candidate) && IsUnder(candidate, install)) exact = candidate;
                }
                games.Add(new GameRecord(name, "Epic", install, exact ?? FindLikelyExe(install), $"EpicManifest:{file}"));
            }
            catch { }
        }
    }

    private static void TryEpicInstalledData(List<GameRecord> games)
    {
        var dat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (!File.Exists(dat)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(dat));
            if (!doc.RootElement.TryGetProperty("InstallationList", out var list) || list.ValueKind != JsonValueKind.Array) return;
            foreach (var e in list.EnumerateArray())
            {
                var path = J(e, "InstallLocation");
                var name = J(e, "AppName");
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    games.Add(new GameRecord(string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name, "Epic", path, FindLikelyExe(path), $"LauncherInstalled:{dat}"));
            }
        }
        catch { }
    }

    private static void TryXbox(List<GameRecord> games)
    {
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
        {
            var root = Path.Combine(d.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var content = Path.Combine(dir, "Content");
                    var install = Directory.Exists(content) ? content : dir;
                    games.Add(new GameRecord(Path.GetFileName(dir), "Xbox", install, FindLikelyExe(install), $"XboxGames:{root}"));
                }
            }
            catch { }
        }
    }

    private static void TryUbisoft(List<GameRecord> games)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", @"SOFTWARE\Ubisoft\Launcher\Installs" })
        {
            try
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                using (var k = key.OpenSubKey(sub))
                {
                    var path = k?.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        games.Add(new GameRecord(Path.GetFileName(path.TrimEnd('\\')), "Ubisoft", path, FindLikelyExe(path), $"UbisoftRegistry:{keyPath}\\{sub}"));
                }
            }
            catch { }
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
                using var k = hive.OpenSubKey(root);
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                using (var s = k.OpenSubKey(sub))
                {
                    var name = s?.GetValue("DisplayName") as string;
                    var pub = s?.GetValue("Publisher") as string;
                    var path = s?.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
                    var hay = $"{name} {pub}";
                    string? launcher =
                        hay.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) ? "EA App" :
                        hay.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) || hay.Contains("Activision", StringComparison.OrdinalIgnoreCase) ? "Battle.net" :
                        hay.Contains("Rockstar", StringComparison.OrdinalIgnoreCase) ? "Rockstar" :
                        hay.Contains("GOG", StringComparison.OrdinalIgnoreCase) ? "GOG" :
                        hay.Contains("Amazon Games", StringComparison.OrdinalIgnoreCase) ? "Amazon Games" : null;
                    if (launcher != null)
                        games.Add(new GameRecord(name, launcher, path, FindLikelyExe(path), $"RegistryFallback:{root}\\{sub}"));
                }
            }
            catch { }
        }
    }

    private static string? FindLikelyExe(string root)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            var candidates = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .Where(p => SafeDepth(root, p) <= 3)
                .Where(p => !Regex.IsMatch(Path.GetFileName(p), "(unins|uninstall|crash|report|launcher|setup|update|updater|redistributable|vc_redist|easyanticheat|eac|battleye|beservice)", RegexOptions.IgnoreCase))
                .Select(p => new
                {
                    Path = p,
                    Score = ExeScore(root, p)
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => SafeLength(x.Path))
                .Take(12)
                .ToArray();
            if (candidates.Length == 0) return null;
            var top = candidates[0];
            return top.Score >= 20 ? top.Path : null;
        }
        catch { return null; }
    }

    private static int ExeScore(string root, string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var rootName = Path.GetFileName(root.TrimEnd('\\', '/')).ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        var flat = name.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        var score = 0;
        if (flat.Contains(rootName, StringComparison.OrdinalIgnoreCase) || rootName.Contains(flat, StringComparison.OrdinalIgnoreCase)) score += 35;
        var depth = SafeDepth(root, path);
        score += depth switch { 0 => 20, 1 => 12, 2 => 6, _ => 0 };
        var len = SafeLength(path);
        if (len >= 20_000_000) score += 20;
        else if (len >= 5_000_000) score += 10;
        if (name.Contains("shipping") || name.EndsWith("-win64-shipping")) score += 12;
        if (name.Contains("client") || name.Contains("game")) score += 5;
        return score;
    }

    private static int SafeDepth(string root, string file)
    {
        try
        {
            var rel = Path.GetRelativePath(root, file);
            return Math.Max(0, rel.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar));
        }
        catch { return int.MaxValue; }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static string J(JsonElement e, string property)
        => e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path; }
    }

    private static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        return p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadRegString(RegistryKey hive, string path, string name)
    {
        try { using var k = hive.OpenSubKey(path); return k?.GetValue(name) as string; }
        catch { return null; }
    }
}
