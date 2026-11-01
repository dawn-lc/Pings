using System.Text;
using Pings.Monitoring;

namespace Pings.Tests;

public sealed class UdpPayloadResolverTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "pings-udp-payload-" + Guid.NewGuid().ToString("N"));

    // 用例断言中文错误提示，固定界面语言才能在英文环境下稳定跑。
    private readonly CultureScope _culture = CultureScope.Chinese();

    public UdpPayloadResolverTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        _culture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string PathInTemp(string name) => Path.Combine(_directory, name);

    [Fact]
    public void 空值使用默认文本()
    {
        Assert.True(UdpPayloadResolver.TryResolve(null, _directory, out byte[] bytes, out string? error));

        Assert.Null(error);
        Assert.Equal(UdpPayloadResolver.DefaultText, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void 普通文本按UTF8编码()
    {
        Assert.True(UdpPayloadResolver.TryResolve("PING-中文", _directory, out byte[] bytes, out string? error));

        Assert.Null(error);
        Assert.Equal(Encoding.UTF8.GetBytes("PING-中文"), bytes);
    }

    [Fact]
    public void Base64解码为二进制()
    {
        byte[] expected = [0x00, 0x01, 0xFF, 0xFE, 0x7F];
        string spec = UdpPayloadResolver.Base64Prefix + Convert.ToBase64String(expected);

        Assert.True(UdpPayloadResolver.TryResolve(spec, _directory, out byte[] bytes, out string? error));

        Assert.Null(error);
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Base64前缀大小写不敏感()
    {
        string spec = "BASE64:" + Convert.ToBase64String([0xAB, 0xCD]);

        Assert.True(UdpPayloadResolver.TryResolve(spec, _directory, out byte[] bytes, out _));
        Assert.Equal([0xAB, 0xCD], bytes);
    }

    [Fact]
    public void 非法Base64报错()
    {
        Assert.False(UdpPayloadResolver.TryResolve(UdpPayloadResolver.Base64Prefix + "***不是base64***", _directory, out _, out string? error));
        Assert.Contains("base64", error);
    }

    [Fact]
    public void Base64前缀后为空报错()
    {
        Assert.False(UdpPayloadResolver.TryResolve(UdpPayloadResolver.Base64Prefix, _directory, out _, out string? error));
        Assert.Contains("没有内容", error);
    }

    [Fact]
    public void 文件前缀读取原始字节()
    {
        string path = PathInTemp("payload.bin");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x00, 0xFF]);

        Assert.True(UdpPayloadResolver.TryResolve(UdpPayloadResolver.FilePrefix + path, _directory, out byte[] bytes, out string? error));

        Assert.Null(error);
        Assert.Equal([0x01, 0x02, 0x03, 0x00, 0xFF], bytes);
    }

    [Fact]
    public void 相对路径以给定目录为基准()
    {
        File.WriteAllBytes(PathInTemp("relative.bin"), [0x42, 0x43]);

        // 故意把基准目录设为 _directory：不依赖测试进程的当前工作目录。
        Assert.True(UdpPayloadResolver.TryResolve("relative.bin", _directory, out byte[] bytes, out string? error));

        Assert.Null(error);
        Assert.Equal([0x42, 0x43], bytes);
    }

    [Fact]
    public void 无前缀但文件存在时按路径处理()
    {
        string name = "auto.bin";
        File.WriteAllBytes(PathInTemp(name), [0x09, 0x08]);

        Assert.True(UdpPayloadResolver.TryResolve(name, _directory, out byte[] bytes, out _));
        Assert.Equal([0x09, 0x08], bytes);
    }

    [Fact]
    public void 无前缀且文件不存在时当作文本()
    {
        Assert.True(UdpPayloadResolver.TryResolve("no-such-file.bin", _directory, out byte[] bytes, out string? error));

        Assert.Null(error);
        Assert.Equal("no-such-file.bin", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void 显式文件前缀但文件不存在时报错()
    {
        Assert.False(UdpPayloadResolver.TryResolve(UdpPayloadResolver.FilePrefix + "missing.bin", _directory, out _, out string? error));
        Assert.Contains("不存在", error);
    }

    [Fact]
    public void 空文件报错()
    {
        string path = PathInTemp("empty.bin");
        File.WriteAllBytes(path, []);

        Assert.False(UdpPayloadResolver.TryResolve(path, _directory, out _, out string? error));
        Assert.Contains("为空", error);
    }

    [Fact]
    public void 超过UDP上限报错()
    {
        string path = PathInTemp("huge.bin");
        File.WriteAllBytes(path, new byte[UdpPayloadResolver.MaxBytes + 1]);

        Assert.False(UdpPayloadResolver.TryResolve(path, _directory, out _, out string? error));
        Assert.Contains("65507", error);
    }

    [Fact]
    public void 校验不返回字节也不会因大文件失败()
    {
        string path = PathInTemp("ok.bin");
        File.WriteAllBytes(path, new byte[1024]);

        Assert.True(UdpPayloadResolver.TryValidate(path, _directory, out string? error));
        Assert.Null(error);
    }
}
