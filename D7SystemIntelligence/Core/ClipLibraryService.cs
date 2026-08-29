using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public sealed record ClipRecord(
    string FileName,
    string FullPath,
    string Extension,
    long SizeBytes,
    double SizeMb,
    DateTime LastWriteTime,
    double? DurationSeconds);

public sealed class ClipLibraryService
{
    private readonly ManagedFfmpegService _ffmpeg = new();
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".webm", ".flv", ".ts", ".m4v"
    };

    public async Task<IReadOnlyList<ClipRecord>> ScanAsync(string folder, bool readDurations = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(x => VideoExtensions.Contains(Path.GetExtension(x)))
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.LastWriteTime)
            .ToArray();

        var result = new List<ClipRecord>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double? duration = null;
            if (readDurations)
            {
                try { duration = await _ffmpeg.GetDurationSecondsAsync(file.FullName, cancellationToken); } catch { }
            }
            result.Add(new ClipRecord(file.Name, file.FullName, file.Extension, file.Length, file.Length / 1024d / 1024d, file.LastWriteTime, duration));
        }
        return result;
    }

    public string Rename(string path, string newNameWithoutExtension)
    {
        ValidateExisting(path);
        var cleaned = SanitizeFileName(newNameWithoutExtension);
        if (string.IsNullOrWhiteSpace(cleaned)) return "الاسم الجديد غير صالح.";
        var folder = Path.GetDirectoryName(path)!;
        var destination = Path.Combine(folder, cleaned + Path.GetExtension(path));
        if (destination.Equals(path, StringComparison.OrdinalIgnoreCase)) return "الاسم لم يتغير.";
        if (File.Exists(destination)) destination = UniquePath(destination);
        File.Move(path, destination);
        return $"تمت إعادة التسمية:\n{destination}";
    }

    public string Move(string path, string destinationFolder)
    {
        ValidateExisting(path);
        if (string.IsNullOrWhiteSpace(destinationFolder)) return "مجلد الوجهة غير صالح.";
        Directory.CreateDirectory(destinationFolder);
        var destination = Path.Combine(destinationFolder, Path.GetFileName(path));
        if (File.Exists(destination)) destination = UniquePath(destination);
        File.Move(path, destination);
        return $"تم نقل المقطع:\n{destination}";
    }

    public string Delete(string path)
    {
        ValidateExisting(path);
        var name = Path.GetFileName(path);
        File.Delete(path);
        return $"تم حذف {name}.";
    }

    public string Reveal(string path)
    {
        ValidateExisting(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
        return "تم فتح موقع المقطع.";
    }

    public async Task<string> TrimFastAsync(string path, TimeSpan start, TimeSpan duration, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateExisting(path);
        if (start < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(start));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        progress?.Report(0);
        var backend = await _ffmpeg.EnsureAsync(progress, cancellationToken);
        if (!backend.Available || backend.FfmpegPath == null) return backend.Detail;

        var folder = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var output = UniquePath(Path.Combine(folder, $"{baseName}_trim{Path.GetExtension(path)}"));
        var startText = start.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var durationText = duration.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var args = $"-hide_banner -y -ss {startText} -i \"{path}\" -t {durationText} -map 0 -c copy -avoid_negative_ts make_zero \"{output}\"";
        var run = await _ffmpeg.RunAsync(args, cancellationToken);
        progress?.Report(100);
        if (run.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            return "فشل القص السريع.\n" + run.Error;
        }
        return $"تم إنشاء نسخة مقصوصة بدون إعادة ترميز (قص سريع قرب keyframe):\n{output}";
    }

    private static void ValidateExisting(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("المقطع غير موجود.", path);
        if (!VideoExtensions.Contains(Path.GetExtension(path))) throw new InvalidOperationException("الملف المحدد ليس فيديو مدعومًا في Clip Library.");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string((value ?? string.Empty).Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(folder, $"{baseName}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }
}
