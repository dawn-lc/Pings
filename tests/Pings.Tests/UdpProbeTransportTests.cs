using System.Net;
using System.Net.Sockets;
using Pings.Monitoring;
using Pings.Monitoring.Transports;

namespace Pings.Tests;

public class UdpProbeTransportTests
{
    private static ProbeTaskOptions Options(int port, int timeoutMs = 1500) => new()
    {
        Name = "udp",
        Target = "127.0.0.1",
        Protocol = ProbeProtocol.Udp,
        Port = port,
        TimeoutMs = timeoutMs
    };

    [Fact]
    public async Task 回声服务判定成功()
    {
        using UdpClient server = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        using CancellationTokenSource cts = new();
        Task echo = Task.Run(() => EchoLoopAsync(server, cts.Token));

        try
        {
            await using IProbeTransport transport = new UdpProbeTransport(Options(port));
            ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

            Assert.Equal(ProbeStatus.Success, result.Status);
            Assert.NotNull(result.RttMs);
        }
        finally
        {
            await cts.CancelAsync();
            await echo;
        }
    }

    [Fact]
    public async Task 无监听端口判定为端口关闭()
    {
        int port;
        using (UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;

        await using IProbeTransport transport = new UdpProbeTransport(Options(port));
        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.PortClosed, result.Status);
    }

    [Fact]
    public async Task 发出的是解析后的字节而不是原始配置文本()
    {
        using UdpClient server = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        byte[] payload = [0x01, 0x02, 0x00, 0xFF];
        TaskCompletionSource<byte[]> captured = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource cts = new();

        Task echo = Task.Run(async () =>
        {
            try
            {
                UdpReceiveResult received = await server.ReceiveAsync(cts.Token);
                captured.TrySetResult(received.Buffer);
                await server.SendAsync(received.Buffer.AsMemory(), received.RemoteEndPoint, cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                captured.TrySetCanceled();
            }
        });

        try
        {
            ProbeTaskOptions withPayload = new()
            {
                Name = "udp",
                Target = "127.0.0.1",
                Protocol = ProbeProtocol.Udp,
                Port = port,
                TimeoutMs = 1500,
                UdpPayload = UdpPayloadResolver.Base64Prefix + Convert.ToBase64String(payload),
                UdpPayloadBytes = payload
            };

            await using IProbeTransport transport = new UdpProbeTransport(withPayload);
            ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

            Assert.Equal(ProbeStatus.Success, result.Status);
            Assert.Equal(payload, await captured.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await cts.CancelAsync();
            await echo;
        }
    }

    [Fact]
    public void 缺少端口时拒绝构造()
    {
        Assert.Throws<ArgumentException>(() => new UdpProbeTransport(new ProbeTaskOptions
        {
            Name = "u",
            Target = "127.0.0.1",
            Protocol = ProbeProtocol.Udp,
            Port = 0
        }));
    }

    private static async Task EchoLoopAsync(UdpClient server, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult received = await server.ReceiveAsync(token);
                await server.SendAsync(received.Buffer.AsMemory(), received.RemoteEndPoint, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
