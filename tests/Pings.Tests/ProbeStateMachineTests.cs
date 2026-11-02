using Pings.Monitoring;

namespace Pings.Tests;

public class ProbeStateMachineTests
{
    private static ProbeTaskOptions Options(
        int lossCount = 5,
        double lossDuration = 30,
        double flapWindow = 300,
        int flapCount = 3,
        int delayThreshold = 20) => new()
        {
            Name = "t",
            Target = "1.1.1.1",
            PacketLossCount = lossCount,
            PacketLossDurationSeconds = lossDuration,
            LossFlapWindowSeconds = flapWindow,
            LossFlapCount = flapCount,
            SignificantDelayThresholdMs = delayThreshold
        };

    private static ProbeResult Timeout() => ProbeResult.Failure(ProbeStatus.Timeout, "请求超时");

    private static ProbeResult Ok(double rtt = 10) => ProbeResult.Success(rtt);

    [Fact]
    public void 默认配置先给疑似再升级为确认()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        List<ProbeEvent> events = [];

        for (int i = 0; i < 5; i++)
        {
            events.AddRange(machine.Apply(Timeout()));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        ProbeEvent suspect = Assert.Single(events, e => e.Kind == ProbeEventKind.LossAlarm);
        Assert.Equal(ProbeSeverity.Warning, suspect.Severity);

        // 第 5 次发生在 t=4s，推进到 t=30s 才满足时长阈值。
        clock.Advance(TimeSpan.FromSeconds(26));

        ProbeEvent[] confirmed = machine.Apply(Timeout());

        ProbeEvent critical = Assert.Single(confirmed, e => e.Kind == ProbeEventKind.LossAlarm);
        Assert.Equal(ProbeSeverity.Critical, critical.Severity);
    }

    [Fact]
    public void 只配置次数时达到次数直接确认()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 3, lossDuration: 0, flapCount: 0), clock);

        List<ProbeEvent> events = [];

        for (int i = 0; i < 3; i++)
            events.AddRange(machine.Apply(Timeout()));

        ProbeEvent alarm = Assert.Single(events, e => e.Kind == ProbeEventKind.LossAlarm);
        Assert.Equal(ProbeSeverity.Critical, alarm.Severity);
    }

    [Fact]
    public void 只配置时长时达到时长直接确认()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 10, flapCount: 0), clock);

        Assert.DoesNotContain(machine.Apply(Timeout()), e => e.Kind == ProbeEventKind.LossAlarm);

        clock.Advance(TimeSpan.FromSeconds(10));

        ProbeEvent[] events = machine.Apply(Timeout());

        ProbeEvent alarm = Assert.Single(events, e => e.Kind == ProbeEventKind.LossAlarm);
        Assert.Equal(ProbeSeverity.Critical, alarm.Severity);
    }

    [Fact]
    public void 两个阈值都为零时不告警()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 0, flapCount: 0), clock);

        List<ProbeEvent> events = [];

        for (int i = 0; i < 50; i++)
        {
            events.AddRange(machine.Apply(Timeout()));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.DoesNotContain(events, e => e.Kind == ProbeEventKind.LossAlarm);
    }

    [Fact]
    public void 恢复后重置并可以再次告警()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(flapCount: 0), clock);

        for (int i = 0; i < 5; i++)
            machine.Apply(Timeout());

        ProbeEvent[] recovered = machine.Apply(Ok(15));
        Assert.Contains(recovered, e => e.Kind == ProbeEventKind.Recovered);

        List<ProbeEvent> again = [];
        for (int i = 0; i < 5; i++)
            again.AddRange(machine.Apply(Timeout()));

        Assert.Contains(again, e => e.Kind == ProbeEventKind.LossAlarm);
    }

    [Fact]
    public void 同一故障类别内的状态变化不重复产生事件()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        ProbeEvent[] first = machine.Apply(Timeout());
        Assert.Contains(first, e => e.Kind == ProbeEventKind.StatusChanged);

        // Timeout → NoReply 同属 NoResponse：不产生状态事件，只留一条 verbose 可见的明细。
        ProbeEvent[] second = machine.Apply(ProbeResult.Failure(ProbeStatus.NoReply, "无回应"));
        Assert.DoesNotContain(second, e => e.Kind == ProbeEventKind.StatusChanged);

        ProbeEvent detail = Assert.Single(second);
        Assert.Equal(ProbeEventKind.StatusDetail, detail.Kind);
        Assert.Equal(ProbeSeverity.Info, detail.Severity);
    }

    [Fact]
    public void 超时与不可达互相翻转只产生明细事件()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        ProbeEvent[] entered = machine.Apply(Timeout());
        ProbeEvent changed = Assert.Single(entered);
        Assert.Equal(ProbeEventKind.StatusChanged, changed.Kind);

        // 设备离线时 ARP 缓存在「有旧条目」与「重新解析失败」之间翻转，
        // 探测结果就会在 请求超时 ↔ 目标主机不可达 之间跳。它们是同一个故障类别。
        for (int i = 0; i < 10; i++)
        {
            ProbeResult result = i % 2 == 0
                ? ProbeResult.Failure(ProbeStatus.Unreachable, "目标主机不可达")
                : Timeout();

            ProbeEvent[] events = machine.Apply(result);

            // 过程中可能夹带中断告警（那是另一条独立逻辑），但不该再有状态事件。
            Assert.DoesNotContain(events, e => e.Kind == ProbeEventKind.StatusChanged);
            Assert.Contains(events, e => e.Kind == ProbeEventKind.StatusDetail);
        }

        // 具体状态仍然实时跟随最新一次结果，只是不再产生状态事件。
        // 循环最后一次（i=9）是 Timeout。
        Assert.Equal(ProbeFaultCategory.NoResponse, machine.Category);
        Assert.Equal(ProbeStatus.Timeout, machine.Status);
    }

    [Fact]
    public void 类别内翻转不覆盖最近事件()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        machine.Apply(Timeout());
        string entered = machine.LastEvent;

        machine.Apply(ProbeResult.Failure(ProbeStatus.Unreachable, "目标主机不可达"));

        // 「最近事件」记的是进入该故障类别的那次状态变化，类别内的细节不该把它改写。
        Assert.Equal(entered, machine.LastEvent);
    }

    [Fact]
    public void 恢复仍然产生恢复事件()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        machine.Apply(Timeout());
        machine.Apply(ProbeResult.Failure(ProbeStatus.Unreachable, "目标主机不可达"));

        ProbeEvent[] recovered = machine.Apply(Ok(15));

        ProbeEvent single = Assert.Single(recovered);
        Assert.Equal(ProbeEventKind.Recovered, single.Kind);
        Assert.Equal(ProbeFaultCategory.None, machine.Category);
    }

    [Fact]
    public void 服务不可用与目标沉默是不同类别()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        machine.Apply(Timeout());

        // 主机给出回应（端口关闭）说明网络路径是通的，这属于新的故障类别，必须记录。
        ProbeEvent[] events = machine.Apply(ProbeResult.Failure(ProbeStatus.PortClosed, "连接被拒绝"));

        Assert.Contains(events, e => e.Kind == ProbeEventKind.StatusChanged);
        Assert.Equal(ProbeFaultCategory.ServiceUnavailable, machine.Category);
    }

    [Fact]
    public void 组内翻转不中断中断升级()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 5, lossDuration: 0, flapCount: 0), clock);

        List<ProbeEvent> events = [];

        // 两种表现交替出现，仍然算「连续无响应」。
        for (int i = 0; i < 5; i++)
        {
            ProbeResult result = i % 2 == 0
                ? Timeout()
                : ProbeResult.Failure(ProbeStatus.Unreachable, "目标主机不可达");

            events.AddRange(machine.Apply(result));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Contains(events, e => e.Kind == ProbeEventKind.LossAlarm);
    }

    [Fact]
    public void 端口关闭属于立即确认的硬故障()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(), clock);

        ProbeEvent[] events = machine.Apply(ProbeResult.Failure(ProbeStatus.PortClosed, "HTTP 503"));

        ProbeEvent statusChanged = Assert.Single(events);
        Assert.Equal(ProbeEventKind.StatusChanged, statusChanged.Kind);
        Assert.Equal(ProbeSeverity.Critical, statusChanged.Severity);
    }

    [Fact]
    public void 窗口内多次短故障判定为抖动链路()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 0, flapWindow: 300, flapCount: 3), clock);

        List<ProbeEvent> events = [];

        for (int i = 0; i < 3; i++)
        {
            events.AddRange(machine.Apply(Timeout()));
            clock.Advance(TimeSpan.FromSeconds(1));
            events.AddRange(machine.Apply(Ok()));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        ProbeEvent flapping = Assert.Single(events, e => e.Kind == ProbeEventKind.Flapping);
        Assert.Equal(ProbeSeverity.Warning, flapping.Severity);
    }

    [Fact]
    public void 已确认的长故障不计入抖动()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 1, lossDuration: 0, flapWindow: 300, flapCount: 1), clock);

        machine.Apply(Timeout());
        clock.Advance(TimeSpan.FromSeconds(5));

        ProbeEvent[] recovered = machine.Apply(Ok());

        Assert.DoesNotContain(recovered, e => e.Kind == ProbeEventKind.Flapping);
    }

    [Fact]
    public void 延迟升高需要连续两个样本确认()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 0, flapCount: 0), clock);

        for (int i = 0; i < 5; i++)
            machine.Apply(Ok(100));

        Assert.DoesNotContain(machine.Apply(Ok(500)), e => e.Kind == ProbeEventKind.DelaySpike);
        Assert.Contains(machine.Apply(Ok(500)), e => e.Kind == ProbeEventKind.DelaySpike);
    }

    [Fact]
    public void 单次毛刺不触发延迟告警()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 0, flapCount: 0), clock);

        for (int i = 0; i < 5; i++)
            machine.Apply(Ok(100));

        Assert.DoesNotContain(machine.Apply(Ok(500)), e => e.Kind == ProbeEventKind.DelaySpike);
        Assert.DoesNotContain(machine.Apply(Ok(100)), e => e.Kind == ProbeEventKind.DelaySpike);
    }

    [Fact]
    public void 恢复当次不判定延迟波动()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 0, flapCount: 0), clock);

        for (int i = 0; i < 5; i++)
            machine.Apply(Ok(100));

        machine.Apply(Timeout());
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(machine.Apply(Ok(900)), e => e.Kind == ProbeEventKind.DelaySpike);
    }

    [Fact]
    public void 关闭延迟阈值后不再产生波动事件()
    {
        ManualTimeProvider clock = new();
        ProbeStateMachine machine = new(Options(lossCount: 0, lossDuration: 0, flapCount: 0, delayThreshold: 0), clock);

        for (int i = 0; i < 5; i++)
            machine.Apply(Ok(100));

        Assert.DoesNotContain(machine.Apply(Ok(5000)), e => e.Kind == ProbeEventKind.DelaySpike);
        Assert.DoesNotContain(machine.Apply(Ok(5000)), e => e.Kind == ProbeEventKind.DelaySpike);
    }
}
