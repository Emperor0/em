using D7.Tools.Models;

namespace D7.Tools.Catalog;

public static class OfficialToolCatalog
{
    public static ToolDescriptor PresentMon { get; } = new(
        Id: "presentmon",
        Name: "PresentMon Console",
        Vendor: "GameTechDev / Intel",
        Version: "2.5.1",
        AssetName: "PresentMon-2.5.1-x64.exe",
        DownloadUri: new Uri("https://github.com/GameTechDev/PresentMon/releases/download/v2.5.1/PresentMon-2.5.1-x64.exe"),
        Sha256: "9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191",
        Integration: "verified-cli",
        License: "MIT");
}
