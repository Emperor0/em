using System.Security.Cryptography;
using D7.Tools.Acquisition;
using Xunit;

namespace D7.UnitTests;

public sealed class ToolAcquisitionTests
{
    [Fact]
    public async Task VerifyHash_AcceptsExactSha256_AndRejectsMismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"d7-hash-{Guid.NewGuid():N}.bin");
        try
        {
            var bytes = "D7-BLACKCORE-integrity-test"u8.ToArray();
            await File.WriteAllBytesAsync(path, bytes);
            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            Assert.True(await ToolAcquisitionService.VerifyHashAsync(path, expected, CancellationToken.None));
            Assert.False(await ToolAcquisitionService.VerifyHashAsync(path, new string('0', 64), CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
