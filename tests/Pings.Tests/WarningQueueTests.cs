using Pings.Monitoring;

namespace Pings.Tests;

public class WarningQueueTests
{
    [Fact]
    public void 先入先出()
    {
        WarningQueue queue = new();

        queue.Enqueue("a");
        queue.Enqueue("b");

        Assert.Equal(2, queue.Count);
        Assert.Equal("a", queue.Peek());
        Assert.Equal("a", queue.Dequeue());
        Assert.Equal("b", queue.Dequeue());
        Assert.Null(queue.Dequeue());
    }

    [Fact]
    public void 超过容量时丢弃最旧的一条()
    {
        WarningQueue queue = new(capacity: 2);

        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Enqueue("c");

        Assert.Equal(2, queue.Count);
        Assert.Equal("b", queue.Peek());
    }

    [Fact]
    public void 空队列查看返回空()
    {
        WarningQueue queue = new();

        Assert.Null(queue.Peek());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void 非法容量抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WarningQueue(0));
    }

    [Fact]
    public void 清空后为空()
    {
        WarningQueue queue = new();
        queue.Enqueue("a");

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Null(queue.Peek());
    }

    [Fact]
    public void 相邻重复消息合并并累计次数()
    {
        WarningQueue queue = new();

        queue.Enqueue("请求超时 10:00:01");
        queue.Enqueue("请求超时 10:00:01");
        queue.Enqueue("请求超时 10:00:01");

        Assert.Equal(1, queue.Count);
        Assert.Equal("请求超时 10:00:01 ×3", queue.Peek());
    }

    [Fact]
    public void 不同消息不会被合并()
    {
        WarningQueue queue = new();

        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Enqueue("a");

        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void 容量溢出时累计丢弃数()
    {
        WarningQueue queue = new(capacity: 2);

        queue.Enqueue("a");
        queue.Enqueue("b");
        queue.Enqueue("c");
        queue.Enqueue("d");

        Assert.Equal(2, queue.Count);
        Assert.Equal(2, queue.Dropped);
    }

    [Fact]
    public void 空消息被忽略()
    {
        WarningQueue queue = new();

        queue.Enqueue("  ");

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task 并发入队不丢数据()
    {
        WarningQueue queue = new(capacity: 10_000);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
                queue.Enqueue($"{worker}-{i}");
        })));

        Assert.Equal(1000, queue.Count);
    }
}
