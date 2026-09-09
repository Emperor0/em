using System.IO.Compression;
using D7.Updater.Validation;

namespace D7.Updater.Security;

public static class SafeZipExtractor
{
    public const int MaxEntries = 10_000;
    public const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const long CompressionRatioCheckThreshold = 10L * 1024 * 1024;
    private const double MaxCompressionRatio = 1_000d;

    public static void Extract(string zipPath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);
        var rootPrefix = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException($"Update archive contains too many entries ({archive.Entries.Count}).");

        long expandedBytes = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var raw = entry.FullName.Replace('\\', '/');
            var isDirectory = raw.EndsWith("/", StringComparison.Ordinal);
            var relative = isDirectory ? raw.TrimEnd('/') : raw;

            if (!UpdateManifestValidator.IsSafeRelativePath(relative))
                throw new InvalidDataException($"Unsafe archive entry path: {entry.FullName}");

            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
                throw new InvalidDataException($"Symbolic links are not allowed in update archives: {entry.FullName}");

            if (!isDirectory)
            {
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaxExpandedBytes)
                    throw new InvalidDataException("Update archive exceeds the expanded-size safety limit.");

                if (entry.Length >= CompressionRatioCheckThreshold && entry.CompressedLength > 0)
                {
                    var ratio = entry.Length / (double)entry.CompressedLength;
                    if (ratio > MaxCompressionRatio)
                        throw new InvalidDataException($"Suspicious compression ratio detected for {entry.FullName}.");
                }
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escaped the destination directory: {entry.FullName}");

            if (!destinations.Add(destinationPath))
                throw new InvalidDataException($"Duplicate archive destination detected: {entry.FullName}");

            if (isDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            using var input = entry.Open();
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
    }
}
