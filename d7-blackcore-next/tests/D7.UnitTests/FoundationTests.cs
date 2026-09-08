using D7.Core.Foundation;
using D7.Core.Models;

namespace D7.UnitTests;

public sealed class FoundationTests
{
    [Fact]
    public void AppPaths_AreSeparatedAndDeterministic()
    {
        var paths = new AppPaths(@"C:\ProgramDataTest", @"C:\LocalDataTest");

        Assert.Equal(@"C:\ProgramDataTest\D7 BLACKCORE", paths.ProgramDataRoot);
        Assert.Equal(@"C:\LocalDataTest\D7 BLACKCORE", paths.LocalDataRoot);
        Assert.Contains(@"C:\ProgramDataTest\D7 BLACKCORE\Transactions", paths.AllDirectories);
        Assert.Contains(@"C:\LocalDataTest\D7 BLACKCORE\Logs", paths.AllDirectories);
    }

    [Fact]
    public void BootstrapResult_IsReady_WhenOnlyNonBlockingCheckFails()
    {
        var checks = new[]
        {
            new BootstrapCheck("windows", "ويندوز", true, true, "ok"),
            new BootstrapCheck("internet", "الإنترنت", false, false, "offline")
        };

        var result = new BootstrapResult(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, checks);

        Assert.True(result.Ready);
    }

    [Fact]
    public void BootstrapResult_IsNotReady_WhenBlockingCheckFails()
    {
        var checks = new[]
        {
            new BootstrapCheck("windows", "ويندوز", false, true, "unsupported")
        };

        var result = new BootstrapResult(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, checks);

        Assert.False(result.Ready);
    }
}
