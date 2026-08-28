using System.Text;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed class CodAdapter
{
    private static readonly string[] Keys =
    [
        "RendererWorkerCount@0;51989;59387",
        "VideoMemoryScaleMP@0;59710;7707",
        "NvidiaReflex@0;33761;11445",
        "ResolutionMultiplier@0;64786;30730",
        "MaxFpsInGame@0;13305;58861",
        "GPUUploadHeaps@0;57752;20945",
        "DLSSModeMP@0;43179;20945"
    ];

    public CodConfigResult Locate()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var roots = new[] { Path.Combine(local, "Activision"), Path.Combine(local, "Call of Duty"), Path.Combine(docs, "Call of Duty"), Path.Combine(local, "Call of Duty", "playersbeta"), Path.Combine(docs, "Call of Duty", "playersbeta") };
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => new FileInfo(f).Length < 5_000_000); } catch { continue; }
            foreach (var file in files)
            {
                try
                {
                    var text = File.ReadAllText(file);
                    if (!text.Contains("RendererWorkerCount@0;51989;59387", StringComparison.OrdinalIgnoreCase)) continue;
                    return new CodConfigResult(true, file, ReadValues(text), "COD config schema matched.");
                } catch { }
            }
        }
        return new CodConfigResult(false, null, new Dictionary<string,string>(), "COD config not found yet. Launch the game once so it creates the player config.");
    }

    public string Optimize(int physicalCores, float vramGb, string mode)
    {
        var located = Locate(); if (!located.Found || located.Path == null) return located.Message;
        if (System.Diagnostics.Process.GetProcessesByName("cod26-cod").Length > 0) return "Close COD before applying config changes.";
        var path = located.Path; var bytes = File.ReadAllBytes(path); var encoding = DetectEncoding(bytes); var text = encoding.GetString(bytes);
        var workers = Math.Clamp(physicalCores - 1, 4, 8);
        var vramScale = vramGb >= 10 ? "0.750000" : vramGb >= 8 ? "0.700000" : "0.620000";
        var targetFps = mode.Equals("Quality", StringComparison.OrdinalIgnoreCase) ? "165" : "300";
        var replacements = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RendererWorkerCount@0;51989;59387"] = workers.ToString(), ["VideoMemoryScaleMP@0;59710;7707"] = vramScale,
            ["NvidiaReflex@0;33761;11445"] = "Enabled", ["ResolutionMultiplier@0;64786;30730"] = "100",
            ["MaxFpsInGame@0;13305;58861"] = targetFps, ["GPUUploadHeaps@0;57752;20945"] = "true", ["DLSSModeMP@0;43179;20945"] = "DLSS"
        };
        var original = text;
        foreach (var kv in replacements)
        {
            var pattern = $"(?m)^({Regex.Escape(kv.Key)}\\s*=\\s*)([^\\r\\n/]+)(.*)$";
            text = Regex.Replace(text, pattern, m => m.Groups[1].Value + kv.Value + m.Groups[3].Value, RegexOptions.IgnoreCase);
        }
        if (text == original) return "COD config found, but supported keys could not be changed safely.";
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "D7SystemIntelligence", "Backups", "COD");
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(backupRoot, Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
        File.Copy(path, backup, false);
        var temp = path + ".d7.tmp";
        File.WriteAllBytes(temp, encoding.GetBytes(text));
        var verify = encoding.GetString(File.ReadAllBytes(temp));
        if (!verify.Contains($"RendererWorkerCount@0;51989;59387 = {workers}", StringComparison.OrdinalIgnoreCase)) { File.Delete(temp); return "Verification failed; original config was not touched."; }
        File.Move(temp, path, true);
        return $"COD optimized safely. Backup: {backup}";
    }

    private static Dictionary<string,string> ReadValues(string text)
    {
        var d = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Keys)
        {
            var m = Regex.Match(text, $"(?m)^{Regex.Escape(key)}\\s*=\\s*([^\\r\\n/]+)", RegexOptions.IgnoreCase);
            if (m.Success) d[key] = m.Groups[1].Value.Trim();
        }
        return d;
    }
    private static Encoding DetectEncoding(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(true);
        return new UTF8Encoding(false);
    }
}
