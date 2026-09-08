using D7.Games.Detection;
using D7.Games.Profiles;
using Xunit;

namespace D7.UnitTests;

public sealed class GameRuntimeTests
{
    [Theory]
    [InlineData("007FirstLight", @"S:\GM\007 First Light\007FirstLight.exe", true)]
    [InlineData("cod", @"S:\GM\Call of Duty\cod.exe", true)]
    [InlineData("SomeGame", @"C:\Program Files (x86)\Steam\steamapps\common\SomeGame\game.exe", true)]
    [InlineData("obs64", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", false)]
    [InlineData("node", @"C:\Program Files\nodejs\node.exe", false)]
    public void KnownGameCatalog_IsConservative(string processName, string path, bool expected)
    {
        Assert.Equal(expected, GameDetectionService.IsKnownGame(processName, path));
    }

    [Fact]
    public void LearnedProfile_PathMatch_IsCaseInsensitive()
    {
        var profile = new LearnedGameProfile(
            "007FirstLight",
            @"S:\GM\007 First Light\007FirstLight.exe",
            "catalog",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.True(GameProfileStore.Matches(profile, "007FIRSTLIGHT.exe", @"s:\gm\007 First Light\007FirstLight.exe"));
        Assert.False(GameProfileStore.Matches(profile, "007FirstLight", @"C:\Other\007FirstLight.exe"));
    }
}
