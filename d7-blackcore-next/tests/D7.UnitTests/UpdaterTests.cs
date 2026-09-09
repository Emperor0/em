using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Updater.Models;
using D7.Updater.Security;
using D7.Updater.Staging;
using D7.Updater.Validation;
using Xunit;

namespace D7.UnitTests;

public sealed class UpdaterTests
{
    [Fact]
    public void ManifestValidator_RejectsHttpAndInvalidHash()
    {
        var manifest = NewManifest(
            new Uri("http://updates.example/d7.zip"),
            "not-a-hash");

        var validation = UpdateManifestValidator.Validate(manifest, 19045);

        Assert.False(validation.Valid);
        Assert.Contains(validation.Errors, x => x.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Errors, x => x.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SafeZipExtractor_BlocksZipSlip()
    {
        var root = NewTempRoot();
        var zip = Path.Combine(root, "evil.zip");
        var destination = Path.Combine(root, "payload");
        var outside = Path.Combine(root, "evil.txt");
        try
        {
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("owned");
            }

            Assert.Throws<InvalidDataException>(() => SafeZipExtractor.Extract(zip, destination));
            Assert.False(File.Exists(outside));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task StageAsync_StagesVerifiedPackageWithoutActivatingIt()
    {
        var root = NewTempRoot();
        try
        {
            var package = CreatePackage(("D7.Blackcore.exe", "development-binary"), ("data/readme.txt", "ok"));
            var hash = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
            var manifest = NewManifest(new Uri("https://updates.example/d7.zip"), hash);
            var paths = new AppPaths(Path.Combine(root, "program"), Path.Combine(root, "local"));
            paths.EnsureCreated();
            var logger = new JsonLineLogger(paths);
            using var http = new HttpClient(new StaticResponseHandler(package));
            var stager = new UpdatePackageStager(paths, logger, http);

            var result = await stager.StageAsync(manifest, CancellationToken.None);

            Assert.True(result.Success, result.TechnicalError);
            Assert.NotNull(result.Staged);
            Assert.True(File.Exists(result.Staged.EntryExecutablePath));
            Assert.True(File.Exists(result.Staged.PendingManifestPath));
            Assert.True(File.Exists(result.Staged.PackagePath));
            Assert.Equal("development-binary", await File.ReadAllTextAsync(result.Staged.EntryExecutablePath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task StageAsync_DeletesBadDownloadAndDoesNotCreatePendingState()
    {
        var root = NewTempRoot();
        try
        {
            var package = CreatePackage(("D7.Blackcore.exe", "wrong"));
            var manifest = NewManifest(new Uri("https://updates.example/d7.zip"), new string('0', 64));
            var paths = new AppPaths(Path.Combine(root, "program"), Path.Combine(root, "local"));
            paths.EnsureCreated();
            var logger = new JsonLineLogger(paths);
            using var http = new HttpClient(new StaticResponseHandler(package));
            var stager = new UpdatePackageStager(paths, logger, http);

            var result = await stager.StageAsync(manifest, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Null(result.Staged);
            Assert.Empty(Directory.Exists(paths.Updates)
                ? Directory.EnumerateFiles(paths.Updates, "pending-update.json", SearchOption.AllDirectories)
                : Array.Empty<string>());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static UpdateManifest NewManifest(Uri url, string hash) => new(
        "D7.Update.v1",
        "D7 BLACKCORE",
        "dev",
        "3.0.0-dev.1",
        19045,
        url,
        hash,
        "zip",
        "D7.Blackcore.exe",
        DateTimeOffset.UtcNow);

    private static byte[] CreatePackage(params (string Path, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path, CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }
        return memory.ToArray();
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7UpdaterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        public StaticResponseHandler(byte[] content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content),
                RequestMessage = request
            };
            response.Content.Headers.ContentLength = _content.LongLength;
            return Task.FromResult(response);
        }
    }
}
