using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Pings.Properties;

namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// HTTP / HTTPS 探测：发一次请求，按状态码判定成功。
    /// </summary>
    /// <remarks>
    /// 计时口径刻意做成「每轮新建连接」（<c>reuseConnection</c> 为 false 时）：
    /// 每一轮都重新走 DNS + TCP + TLS，得到的 RTT 才和 ICMP / TCP 探测可比。
    /// 开启 <c>reuseConnection</c> 后测的是应用层往返，适合观察服务处理时间，
    /// 两种口径不能放进同一张表里横向比较。
    ///
    /// 其他取舍：
    /// <list type="bullet">
    /// <item>默认不使用系统代理：否则量到的是代理的 RTT；</item>
    /// <item>默认不跟随重定向：3xx 计为成功但保留状态码，避免把跳转链路的耗时算进目标；</item>
    /// <item>HEAD 返回 405 时永久改用 GET——部分服务不实现 HEAD，每轮都报 405 没有意义。</item>
    /// </list>
    /// </remarks>
    public sealed class HttpProbeTransport : IProbeTransport
    {
        private readonly ProbeTaskOptions _options;
        private readonly Uri _uri;
        private readonly HttpClient _client;
        private string _method;
        private double? _connectMs;

        public HttpProbeTransport(ProbeTaskOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (!ProbeTaskOptions.IsValidHttpUrl(options.Target))
                throw new ArgumentException(string.Format(Resources.HttpTargetInvalid, options.Target), nameof(options));

            _uri = new Uri(options.Target, UriKind.Absolute);
            _method = ProbeTaskOptions.NormalizeHttpMethod(options.HttpMethod);

            SocketsHttpHandler handler = new()
            {
                ConnectTimeout = options.Timeout,
                UseProxy = options.UseProxy,
                AllowAutoRedirect = options.FollowRedirects,
                AutomaticDecompression = DecompressionMethods.None,
                MaxConnectionsPerServer = 1,
                PooledConnectionLifetime = options.ReuseConnection ? TimeSpan.FromMinutes(2) : TimeSpan.Zero,
                PooledConnectionIdleTimeout = options.ReuseConnection ? TimeSpan.FromMinutes(1) : TimeSpan.Zero,

                // 自己建连才能拿到精确的连接耗时，并据此区分「建连超时」与「响应超时」。
                ConnectCallback = ConnectAsync
            };

            _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async ValueTask<ProbeResult> ProbeAsync(CancellationToken token)
        {
            _connectMs = null;
            ProbeResult result = await SendAsync(token).ConfigureAwait(false);

            if (_method == "HEAD" && IsMethodNotAllowed(result))
            {
                _method = "GET";
                _connectMs = null;
                result = await SendAsync(token).ConfigureAwait(false);
            }

            return result;
        }

        private static bool IsMethodNotAllowed(ProbeResult result)
            => result.Status == ProbeStatus.HttpStatusUnexpected
                && result.Detail is { } detail
                && detail.StartsWith("HTTP 405", StringComparison.Ordinal);

        private async ValueTask<ProbeResult> SendAsync(CancellationToken token)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_options.TimeoutMs);

            long started = Stopwatch.GetTimestamp();

            try
            {
                using HttpRequestMessage request = new(new HttpMethod(_method), _uri);

                if (!_options.ReuseConnection)
                    request.Headers.ConnectionClose = true;

                using HttpResponseMessage response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                int code = (int)response.StatusCode;

                return IsExpected(code)
                    ? ProbeResult.Success(elapsed, $"HTTP {code}", _connectMs)
                    : ProbeResult.Failure(ProbeStatus.HttpStatusUnexpected, $"HTTP {code}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return _connectMs is null
                    ? ProbeResult.Failure(ProbeStatus.Timeout, string.Format(Resources.TransportConnectTimeout, _options.TimeoutMs))
                    : ProbeResult.Failure(ProbeStatus.ResponseTimeout, string.Format(Resources.TransportResponseTimeout, _options.TimeoutMs));
            }
            catch (HttpRequestException ex)
            {
                return MapRequestException(ex);
            }
            catch (Exception ex)
            {
                return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);
            }
        }

        private ProbeResult MapRequestException(HttpRequestException exception)
        {
            ProbeStatus? mapped = exception.HttpRequestError switch
            {
                HttpRequestError.SecureConnectionError => ProbeStatus.TlsFailure,
                HttpRequestError.NameResolutionError => ProbeStatus.DnsFailure,
                HttpRequestError.HttpProtocolError => ProbeStatus.ProtocolError,
                HttpRequestError.InvalidResponse => ProbeStatus.ProtocolError,
                HttpRequestError.ResponseEnded => ProbeStatus.ProtocolError,
                _ => null
            };

            if (mapped is { } status)
                return ProbeResult.Failure(status, exception.Message);

            if (SocketErrorMapping.Find(exception) is { } socket)
                return ProbeResult.Failure(SocketErrorMapping.Map(socket.SocketErrorCode), SocketErrorMapping.Describe(socket.SocketErrorCode));

            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is AuthenticationException)
                    return ProbeResult.Failure(ProbeStatus.TlsFailure, exception.Message);
            }

            return ProbeResult.Failure(ProbeStatus.LocalError, exception.Message);
        }

        private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
        {
            Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                long started = Stopwatch.GetTimestamp();
                await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                _connectMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private bool IsExpected(int code)
        {
            if (_options.ExpectedStatus.Count == 0)
                return code is >= 200 and <= 399;

            for (int i = 0; i < _options.ExpectedStatus.Count; i++)
            {
                if (_options.ExpectedStatus[i] == code)
                    return true;
            }

            return false;
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
