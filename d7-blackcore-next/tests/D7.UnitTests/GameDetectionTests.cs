using D7.Games.Detection;
using Xunit;

namespace D7.UnitTests;

public sealed class GameDetectionTests
{
    private readonly GameProcessClassifier _classifier = new();

    [Theory]
    [InlineData("obs64", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe")]
    [InlineData("TikTok LIVE Studio", @"C:\Program Files\TikTok LIVE Studio\TikTok LIVE Studio.exe")]
    [InlineData("python", @"C:\Python\python.exe")]
    [InlineData("node", @"C:\Program Files\nodejs\node.exe")]
    [InlineData("D7Agent", @"C:\D7 Agent\D7Agent.exe")]
    public void KnownFalseProcesses_AreNeverGames(string name, string path)
    {
        var result = _classifier.Classify(new GameProcessFacts(10, name, path, true, true, true));
        Assert.False(result.IsGame);
        Assert.True(result.Score < 0);
    }

    [Fact]
    public void LearnedRealGame_WithForeground_IsGame()
    {
        var result = _classifier.Classify(new GameProcessFacts(
            777,
            "007FirstLight",
            @"S:\GM\007 First Light\007FirstLight.exe",
            IsForeground: true,
            HasGraphicsActivity: true,
            IsLearned: true,
            IsKnownGameInstallPath: true));

        Assert.True(result.IsGame);
        Assert.Equal("عالٍ", result.ConfidenceAr);
    }

    [Fact]
    public void SteamLauncher_IsNotGameEvenWhenForeground()
    {
        var result = _classifier.Classify(new GameProcessFacts(20, "steam", @"C:\Program Files (x86)\Steam\steam.exe", true, true, false));
        Assert.False(result.IsGame);
    }
}
