using System.Net;
using System.Net.Sockets;
using Pings.Monitoring;
using Pings.Monitoring.Transports;

namespace Pings.Tests;

public class TcpProbeTransportTests
{
    private static ProbeTaskOptions Options(int port, int timeoutMs = 2000) => new()
    {
        Name = "tcp",
        Target = "127.0.0.1",
        Protocol = ProbeProtocol.Tcp,
        Port = port,
        TimeoutMs = timeoutMs
    };

    [Fact]
    public async Task 可连接的端口判定成功()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Task accept = Task.Run(async () =>
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
            });

            await using IProbeTransport transport = new TcpProbeTransport(Options(port));
            ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

            Assert.Equal(ProbeStatus.Success, result.Status);
            Assert.NotNull(result.RttMs);
            Assert.NotNull(result.ConnectMs);

            await accept;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task 关闭的端口判定为端口关闭()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        // 本机已关闭的端口在这台机器上要约 2 秒才回 RST，超时给宽一点。
        await using IProbeTransport transport = new TcpProbeTransport(Options(port, timeoutMs: 8000));
        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.PortClosed, result.Status);
    }

    [Fact]
    public void 缺少端口时拒绝构造()
    {
        Assert.Throws<ArgumentException>(() => new TcpProbeTransport(new ProbeTaskOptions
        {
            Name = "t",
            Target = "127.0.0.1",
            Protocol = ProbeProtocol.Tcp,
            Port = 0
        }));
    }
}
