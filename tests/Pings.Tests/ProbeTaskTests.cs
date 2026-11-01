using Pings.Monitoring;
using Pings.Monitoring.Transports;

namespace Pings.Tests;

public class ProbeTaskTests
{
    private sealed class SequenceTransport(params ProbeResult[] results) : IProbeTransport
    {
        private int _index;

        public int Calls { get; private set; }

        public bool Disposed { get; private set; }

        public ValueTask<ProbeResult> ProbeAsync(CancellationToken token)
        {
            Calls++;
            ProbeResult result = results[Math.Min(_index++, results.Length - 1)];
            return ValueTask.FromResult(result);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingTransport : IProbeTransport
    {
        public ValueTask<ProbeResult> ProbeAsync(CancellationToken token)
            => throw new InvalidOperationException("boom");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ProbeTaskOptions Options() => new()
    {
        Name = "t",
        Target = "1.1.1.1",
        SampleWindow = 8,
        PacketLossCount = 2,
        PacketLossDurationSeconds = 0,
        LossFlapCount = 0
    };

    [Fact]
    public async Task 单次探测更新快照与统计()
    {
        SequenceTransport transport = new(ProbeResult.Success(12));
        await using ProbeTask task = new(Options(), transport);

        await task.ProbeOnceAsync();

        ProbeTaskSnapshot snapshot = task.Snapshot();

        Assert.Equal(ProbeStatus.Success, snapshot.Status);
        Assert.Equal(12, snapshot.DelayMs);
        Assert.Equal(1, snapshot.Statistics.Sent);
        Assert.Equal(1, snapshot.Statistics.Received);
        Assert.Equal(ProbeFaultCategory.None, snapshot.Category);
    }

    [Fact]
    public async Task 类别变化与恢复各产生一次事件()
    {
        SequenceTransport transport = new(
            ProbeResult.Failure(ProbeStatus.Timeout, "请求超时"),
            ProbeResult.Success(8));

        await using ProbeTask task = new(Options(), transport);

        List<ProbeEvent> events = [];
        task.EventRaised += (_, e) => events.Add(e.Event);

        await task.ProbeOnceAsync();
        await task.ProbeOnceAsync();

        Assert.Contains(events, e => e.Kind == ProbeEventKind.StatusChanged);
        Assert.Contains(events, e => e.Kind == ProbeEventKind.Recovered);
    }

    [Fact]
    public async Task 传输层异常降级为本地故障()
    {
        await using ProbeTask task = new(Options(), new ThrowingTransport());

        await task.ProbeOnceAsync();

        ProbeTaskSnapshot snapshot = task.Snapshot();

        Assert.Equal(ProbeStatus.LocalError, snapshot.Status);
        Assert.Contains("boom", snapshot.StatusText);
    }

    [Fact]
    public async Task 达到次数阈值时发出中断告警()
    {
        SequenceTransport transport = new(ProbeResult.Failure(ProbeStatus.Timeout, "请求超时"));
        await using ProbeTask task = new(Options(), transport);

        List<ProbeEvent> events = [];
        task.EventRaised += (_, e) => events.Add(e.Event);

        await task.ProbeOnceAsync();
        await task.ProbeOnceAsync();

        Assert.Contains(events, e => e.Kind == ProbeEventKind.LossAlarm);
    }

    [Fact]
    public async Task 释放时释放传输层()
    {
        SequenceTransport transport = new(ProbeResult.Success(1));
        ProbeTask task = new(Options(), transport);

        await task.DisposeAsync();

        Assert.True(transport.Disposed);
    }
}
