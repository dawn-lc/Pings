using System.Net;
using System.Net.Sockets;
using System.Text;
using Pings.Monitoring;
using Pings.Monitoring.Transports;

namespace Pings.Tests;

public class HttpProbeTransportTests
{
    private const string OkResponse = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
    private const string UnavailableResponse = "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
    private const string MethodNotAllowedResponse = "HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    private static ProbeTaskOptions Options(
        int port,
        int timeoutMs = 2000,
        string method = "HEAD",
        IReadOnlyList<int>? expected = null) => new()
        {
            Name = "http",
            Target = $"http://127.0.0.1:{port}/health",
            Protocol = ProbeProtocol.Http,
            TimeoutMs = timeoutMs,
            HttpMethod = method,
            ExpectedStatus = expected ?? []
        };

    [Fact]
    public async Task 状态码200判定成功()
    {
        await using LoopbackHttpServer server = new(_ => OkResponse);
        await using IProbeTransport transport = new HttpProbeTransport(Options(server.Port));

        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.Success, result.Status);
        Assert.Equal("HTTP 200", result.Detail);
        Assert.NotNull(result.ConnectMs);
    }

    [Fact]
    public async Task 状态码不在期望集合内判定为状态码异常()
    {
        await using LoopbackHttpServer server = new(_ => UnavailableResponse);
        await using IProbeTransport transport = new HttpProbeTransport(Options(server.Port));

        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.HttpStatusUnexpected, result.Status);
        Assert.StartsWith("HTTP 503", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 配置了期望状态码时按集合精确匹配()
    {
        await using LoopbackHttpServer server = new(_ => OkResponse);
        await using IProbeTransport transport = new HttpProbeTransport(Options(server.Port, expected: [204]));

        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.HttpStatusUnexpected, result.Status);
    }

    [Fact]
    public async Task 响应延迟超过超时判定为响应超时()
    {
        await using LoopbackHttpServer server = new(_ => OkResponse, TimeSpan.FromSeconds(5));
        await using IProbeTransport transport = new HttpProbeTransport(Options(server.Port, timeoutMs: 400));

        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.ResponseTimeout, result.Status);
    }

    [Fact]
    public async Task HEAD被拒绝时自动改用GET()
    {
        await using LoopbackHttpServer server = new(
            requestLine => requestLine.StartsWith("HEAD ", StringComparison.Ordinal)
                ? MethodNotAllowedResponse
                : OkResponse);

        await using IProbeTransport transport = new HttpProbeTransport(Options(server.Port));

        ProbeResult result = await transport.ProbeAsync(CancellationToken.None);

        Assert.Equal(ProbeStatus.Success, result.Status);
        Assert.Equal("HTTP 200", result.Detail);
    }

    [Fact]
    public void 非法地址时拒绝构造()
    {
        Assert.Throws<ArgumentException>(() => new HttpProbeTransport(new ProbeTaskOptions
        {
            Name = "h",
            Target = "ftp://example.com",
            Protocol = ProbeProtocol.Http
        }));
    }

    /// <summary>
    /// 最小 HTTP 服务：读掉请求头，按请求行决定响应。
    /// 不用 <see cref="HttpListener" /> 是为了避开 Windows 上的 URL ACL 要求。
    /// </summary>
    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly Func<string, string> _respond;
        private readonly TimeSpan _delay;

        public LoopbackHttpServer(Func<string, string> respond, TimeSpan? delay = null)
        {
            _respond = respond;
            _delay = delay ?? TimeSpan.Zero;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client), CancellationToken.None);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    using StreamReader reader = new(
                        stream,
                        Encoding.ASCII,
                        detectEncodingFromByteOrderMarks: false,
                        bufferSize: 1024,
                        leaveOpen: true);

                    string requestLine = await reader.ReadLineAsync() ?? string.Empty;

                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                    {
                    }

                    if (_delay > TimeSpan.Zero)
                        await Task.Delay(_delay);

                    byte[] response = Encoding.ASCII.GetBytes(_respond(requestLine));
                    await stream.WriteAsync(response);
                    await stream.FlushAsync();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();

            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }

            _cts.Dispose();
        }
    }
}
