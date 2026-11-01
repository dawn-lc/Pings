using Pings.Diagnostics;

namespace Pings.Tests;

public sealed class RotatingLogFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "pings-log-tests-" + Guid.NewGuid().ToString("N"));

    public RotatingLogFileTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void 写入内容可见()
    {
        string path = Path.Combine(_directory, "app.log");

        using (RotatingLogFile file = new(path, maxBytes: 10_000, maxFiles: 3))
        {
            file.WriteLine("第一行");
            file.WriteLine("second line");
        }

        string content = File.ReadAllText(path);
        Assert.Contains("第一行", content);
        Assert.Contains("second line", content);
    }

    [Fact]
    public void 超过体积后轮转且保留数量受限()
    {
        string path = Path.Combine(_directory, "app.log");
        string line = new('x', 80);

        using (RotatingLogFile file = new(path, maxBytes: 200, maxFiles: 2))
        {
            for (int i = 0; i < 20; i++)
                file.WriteLine($"{i:D2}-{line}");
        }

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_directory, "app.1.log")));

        string[] archives = Directory.GetFiles(_directory, "app.*.log");
        Assert.True(archives.Length <= 2, $"历史文件数量应不超过 2，实际 {archives.Length}");
        Assert.DoesNotContain(archives, file => file.EndsWith("app.3.log", StringComparison.Ordinal));
    }

    [Fact]
    public void 追加而不是覆盖已有文件()
    {
        string path = Path.Combine(_directory, "append.log");
        File.WriteAllText(path, "old" + Environment.NewLine);

        using (RotatingLogFile file = new(path, maxBytes: 1_000_000, maxFiles: 2))
        {
            file.WriteLine("new");
        }

        string content = File.ReadAllText(path);
        Assert.Contains("old", content);
        Assert.Contains("new", content);
    }

    [Fact]
    public void 目录不存在时自动创建()
    {
        string path = Path.Combine(_directory, "nested", "deep", "app.log");

        using (RotatingLogFile file = new(path, maxBytes: 1_000_000, maxFiles: 2))
        {
            file.WriteLine("hello");
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void 非法参数抛异常()
    {
        Assert.Throws<ArgumentException>(() => new RotatingLogFile("", 100, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingLogFile(Path.Combine(_directory, "a.log"), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingLogFile(Path.Combine(_directory, "a.log"), 100, 0));
    }

    [Fact]
    public void Dispose后不再写入()
    {
        string path = Path.Combine(_directory, "closed.log");
        RotatingLogFile file = new(path, maxBytes: 1_000_000, maxFiles: 1);

        file.Dispose();
        file.WriteLine("ignored");

        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
