namespace SiteSense.Api.Helpers;

// Simulated clock for test mode (Q38): GetUtcNow stays at one instant until replaced. Timers and GetTimestamp keep real time.
public sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
