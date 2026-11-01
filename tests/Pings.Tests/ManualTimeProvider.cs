namespace Pings.Tests;

/// <summary>
/// 可手动推进的 <see cref="TimeProvider" />：状态机所有时长判定都走它，
/// 测试不必真的等待。
/// </summary>
internal sealed class ManualTimeProvider(TimeSpan? start = null) : TimeProvider
{
    private long _timestamp = (start ?? TimeSpan.Zero).Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow()
        => new(DateTimeOffset.UnixEpoch.UtcTicks + _timestamp, TimeSpan.Zero);

    public void Advance(TimeSpan delta) => _timestamp += delta.Ticks;
}
