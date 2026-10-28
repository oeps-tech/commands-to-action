namespace Oeps.CommandToAction.Core.Commands;

public sealed record ActionTimings
{
    public int InitMs { get; init; } = 1000;
    public int SnMs { get; init; } = 100;
    public int TabMs { get; init; } = 200;
    public int CharMs { get; init; } = 100;
    public int BetweenActionMs { get; init; } = 250;

    public void Validate()
    {
        Check(InitMs, nameof(InitMs));
        Check(SnMs, nameof(SnMs));
        Check(TabMs, nameof(TabMs));
        Check(CharMs, nameof(CharMs));
        Check(BetweenActionMs, nameof(BetweenActionMs));
    }

    private static void Check(int value, string name)
    {
        if (value is < 0 or > 60000)
            throw new ArgumentOutOfRangeException(name, value, "Timing must be between 0 and 60,000 milliseconds.");
    }
}
