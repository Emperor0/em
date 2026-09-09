using System.Text.RegularExpressions;
using D7.Updater.Models;

namespace D7.Updater.Validation;

public static class UpdateManifestValidator
{
    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SemVerRegex = new("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> Channels = new(StringComparer.OrdinalIgnoreCase) { "dev", "beta", "rc", "stable" };

    public static UpdateManifestValidation Validate(UpdateManifest manifest, int currentWindowsBuild)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();

        if (!string.Equals(manifest.Schema, "D7.Update.v1", StringComparison.Ordinal))
            errors.Add("مخطط ملف التحديث غير مدعوم.");

        if (!string.Equals(manifest.Product, "D7 BLACKCORE", StringComparison.OrdinalIgnoreCase))
            errors.Add("ملف التحديث لا يخص D7 BLACKCORE.");

        if (!Channels.Contains(manifest.Channel))
            errors.Add("قناة التحديث غير معروفة.");

        if (!SemVerRegex.IsMatch(manifest.Version ?? string.Empty))
            errors.Add("رقم الإصدار ليس Semantic Version صالحًا.");

        if (manifest.MinimumWindowsBuild <= 0)
            errors.Add("الحد الأدنى لإصدار Windows غير صالح.");
        else if (currentWindowsBuild > 0 && currentWindowsBuild < manifest.MinimumWindowsBuild)
            errors.Add($"هذا التحديث يحتاج Windows build {manifest.MinimumWindowsBuild} أو أحدث.");

        if (manifest.PackageUrl is null || !manifest.PackageUrl.IsAbsoluteUri || !string.Equals(manifest.PackageUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            errors.Add("رابط حزمة التحديث يجب أن يكون HTTPS مطلقًا.");

        if (!Sha256Regex.IsMatch(manifest.Sha256 ?? string.Empty))
            errors.Add("بصمة SHA-256 غير صالحة.");

        if (!string.Equals(manifest.PackageType, "zip", StringComparison.OrdinalIgnoreCase))
            errors.Add("نوع حزمة التحديث غير مدعوم.");

        if (!IsSafeRelativePath(manifest.EntryExecutable) || !manifest.EntryExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            errors.Add("مسار ملف التشغيل داخل الحزمة غير آمن أو غير صالح.");

        if (manifest.PublishedUtc == default)
            errors.Add("تاريخ نشر التحديث غير صالح.");

        return new UpdateManifestValidation(errors.Count == 0, errors);
    }

    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/', StringComparison.Ordinal)) return false;
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        return parts.All(part => part is not "." and not ".." && !part.Contains(':'));
    }
}
