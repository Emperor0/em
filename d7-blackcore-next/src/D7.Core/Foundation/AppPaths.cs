namespace D7.Core.Foundation;

public sealed class AppPaths
{
    public AppPaths(string? programData = null, string? localAppData = null)
    {
        ProgramDataRoot = Path.Combine(
            programData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "D7 BLACKCORE");
        LocalDataRoot = Path.Combine(
            localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D7 BLACKCORE");
    }

    public string ProgramDataRoot { get; }
    public string LocalDataRoot { get; }
    public string Config => Path.Combine(ProgramDataRoot, "Config");
    public string Profiles => Path.Combine(ProgramDataRoot, "Profiles");
    public string Measurements => Path.Combine(ProgramDataRoot, "Measurements");
    public string Transactions => Path.Combine(ProgramDataRoot, "Transactions");
    public string Tools => Path.Combine(ProgramDataRoot, "Tools");
    public string IntelligenceCache => Path.Combine(ProgramDataRoot, "IntelligenceCache");
    public string Logs => Path.Combine(LocalDataRoot, "Logs");
    public string Diagnostics => Path.Combine(LocalDataRoot, "Diagnostics");
    public string Updates => Path.Combine(LocalDataRoot, "Updates");

    public IReadOnlyList<string> AllDirectories =>
    [
        ProgramDataRoot, LocalDataRoot, Config, Profiles, Measurements, Transactions,
        Tools, IntelligenceCache, Logs, Diagnostics, Updates
    ];

    public void EnsureCreated()
    {
        foreach (var directory in AllDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }
}
