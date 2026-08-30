using System.Text.Json;

namespace D7SystemIntelligence.Core;

public enum GameKind
{
    Auto,
    Competitive,
    Story,
    General
}

public sealed record GameIdentityOverride(
    string InstallPath,
    string ExecutablePath,
    GameKind Kind,
    DateTimeOffset ConfirmedAt);

public sealed class GameIdentityStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public GameIdentityStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Games");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "verified-identities.json");
    }

    public IReadOnlyDictionary<string, GameIdentityOverride> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, GameIdentityOverride>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var list = JsonSerializer.Deserialize<List<GameIdentityOverride>>(File.ReadAllText(_path), JsonOptions) ?? [];
            return list.Where(x => Directory.Exists(x.InstallPath) && File.Exists(x.ExecutablePath))
                .GroupBy(x => Normalize(x.InstallPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ConfirmedAt).First(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, GameIdentityOverride>(StringComparer.OrdinalIgnoreCase); }
    }

    public string Confirm(string installPath, string executablePath, GameKind kind)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return "Install path غير موجود.";
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath) || !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "اختر EXE موجودًا داخل تثبيت اللعبة.";

        var fullInstall = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullExe = Path.GetFullPath(executablePath);
        if (!IsUnder(fullExe, fullInstall)) return "D7KT رفض الهوية: EXE خارج مجلد تثبيت اللعبة.";

        var all = LoadRaw();
        all.RemoveAll(x => Normalize(x.InstallPath).Equals(Normalize(fullInstall), StringComparison.OrdinalIgnoreCase));
        all.Add(new GameIdentityOverride(fullInstall, fullExe, kind, DateTimeOffset.Now));
        Save(all);
        return $"Verified identity saved • {Path.GetFileName(fullExe)} • {kind}.";
    }

    public string Remove(string installPath)
    {
        var all = LoadRaw();
        var removed = all.RemoveAll(x => Normalize(x.InstallPath).Equals(Normalize(installPath), StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return "لا توجد هوية مؤكدة لهذه اللعبة.";
        Save(all);
        return "تم حذف User-confirmed identity وسيعود D7KT لاكتشاف المنصة.";
    }

    public GameRecord Apply(GameRecord game)
    {
        var map = Load();
        return map.TryGetValue(Normalize(game.InstallPath), out var id)
            ? game with { ExecutablePath = id.ExecutablePath, Source = game.Source + "|D7Verified" }
            : game;
    }

    public GameKind KindFor(GameRecord game)
    {
        var map = Load();
        return map.TryGetValue(Normalize(game.InstallPath), out var id) ? id.Kind : GameKind.Auto;
    }

    private List<GameIdentityOverride> LoadRaw()
    {
        if (!File.Exists(_path)) return [];
        try { return JsonSerializer.Deserialize<List<GameIdentityOverride>>(File.ReadAllText(_path), JsonOptions) ?? []; }
        catch { return []; }
    }

    private void Save(IEnumerable<GameIdentityOverride> list)
        => File.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOptions));

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path?.Trim() ?? string.Empty; }
    }

    private static bool IsUnder(string path, string root)
        => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
