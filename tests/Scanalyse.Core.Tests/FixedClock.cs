namespace Scanalyse.Core.Tests;

/// <summary>Horloge figée pour rendre les tests de dates reproductibles.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public static readonly FixedClock October2026 = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
