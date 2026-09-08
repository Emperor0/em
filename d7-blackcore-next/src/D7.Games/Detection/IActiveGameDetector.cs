namespace D7.Games.Detection;

public interface IActiveGameDetector
{
    Task<ActiveGame?> DetectActiveGameAsync(CancellationToken cancellationToken);
}
