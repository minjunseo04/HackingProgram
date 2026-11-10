using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace BoundaryCheck;

internal sealed record LocalFixtureResult(
    string Scenario,
    string Identity,
    string Method,
    string Path,
    string Expected,
    string Actual,
    int? HttpStatusCode,
    int? ControlHttpStatusCode,
    string Verdict,
    string Note);

internal static class LocalFixtureRunner
{
    private static readonly FixtureCheck[] Checks =
    [
        new("자기 조직 문서 열람", "Acme 일반 사용자", "acme-user", "/api/documents/acme-plan", "허용", null),
        new("다른 조직 문서 차단", "Birch 일반 사용자", "birch-user", "/api/documents/acme-plan", "거부", "acme-user"),
        new("존재하지 않는 문서", "Birch 일반 사용자", "birch-user", "/api/documents/missing-doc", "거부", "acme-user"),
    ];

    public static async Task<IReadOnlyList<LocalFixtureResult>> RunAsync(bool vulnerable, CancellationToken cancellationToken)
    {
        await using var fixture = new LoopbackFixtureServer(vulnerable);
        fixture.Start();

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var results = new List<LocalFixtureResult>(Checks.Length);

        foreach (var check in Checks)
        {
            try
            {
                int? controlStatus = null;
                if (check.ControlIdentity is not null)
                {
                    controlStatus = await SendGetAsync(client, fixture, check.ControlIdentity, check.Path, cancellationToken);
                }

                var actualStatus = await SendGetAsync(client, fixture, check.IdentityId, check.Path, cancellationToken);
                var (verdict, note) = Evaluate(check.Expected, controlStatus, actualStatus);
                results.Add(new LocalFixtureResult(
                    check.Title,
                    check.IdentityLabel,
                    "GET",
                    check.Path,
                    check.Expected,
                    actualStatus.ToString(CultureInfo.InvariantCulture),
                    actualStatus,
                    controlStatus,
                    verdict,
                    note));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                results.Add(new LocalFixtureResult(
                    check.Title,
                    check.IdentityLabel,
                    "GET",
                    check.Path,
                    check.Expected,
                    "응답 없음",
                    null,
                    null,
                    "판정 보류",
                    "로컬 예제 요청을 완료하지 못했습니다. 응답 원문은 보관하지 않습니다."));
            }
        }

        return results;
    }

    private static async Task<int> SendGetAsync(
        HttpClient client,
        LoopbackFixtureServer fixture,
        string identityId,
        string path,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://127.0.0.1:{fixture.Port}{path}", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-BoundaryCheck-Fixture", fixture.Guard);
        request.Headers.Add("X-BoundaryCheck-Identity", identityId);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return (int)response.StatusCode;
    }

    private static (string Verdict, string Note) Evaluate(string expected, int? controlStatus, int actualStatus)
    {
        if (expected == "허용")
        {
            if (actualStatus is >= 200 and < 300)
                return ("기대와 일치", "예제 신원이 자기 조직 문서를 읽었습니다.");
            if (actualStatus >= 500)
                return ("판정 보류", "예제 서버 오류만으로 접근 규칙을 판정할 수 없습니다.");
            return ("검토 필요", "읽기 허용을 기대했지만 성공 응답을 받지 못했습니다.");
        }

        if (controlStatus is null)
            return ("판정 보류", "소유자 대조 요청이 없어 자료 존재 여부를 확인하지 못했습니다.");
        if (controlStatus == 404)
            return ("판정 보류", "소유자 대조 요청도 404여서 자료가 존재하는지 확인할 수 없습니다.");
        if (controlStatus is < 200 or >= 300)
            return ("판정 보류", "소유자 대조 요청이 성공하지 않아 차단 결과를 비교할 수 없습니다.");

        if (actualStatus is 401 or 403 or 404)
            return ("기대와 일치", "소유자 대조는 성공했고 다른 조직 신원은 차단 응답을 받았습니다.");
        if (actualStatus is >= 200 and < 300)
            return ("검토 필요", "다른 조직 신원이 문서를 읽었습니다. 예제의 접근 차단이 실패했습니다.");
        return ("판정 보류", "예상한 허용·차단 응답이 아니어서 결과를 판정하지 않았습니다.");
    }

    private sealed record FixtureCheck(
        string Title,
        string IdentityLabel,
        string IdentityId,
        string Path,
        string Expected,
        string? ControlIdentity);

    private sealed class LoopbackFixtureServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly bool _vulnerable;
        private Task? _serveTask;

        public string Guard { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public int Port { get; private set; }

        public LoopbackFixtureServer(bool vulnerable) => _vulnerable = vulnerable;

        public void Start()
        {
            _listener.Start(8);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serveTask = ServeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            if (_serveTask is not null)
            {
                try { await _serveTask; }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
            }
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (_stop.IsCancellationRequested) { break; }

                using (client)
                {
                    try { await HandleClientAsync(client.GetStream(), _stop.Token); }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                    catch { /* The fixture never logs request headers or response content. */ }
                }
            }
        }

        private async Task HandleClientAsync(NetworkStream stream, CancellationToken stoppingToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var token = timeout.Token;
            var requestLine = await ReadLineAsync(stream, 4096, token);
            if (requestLine is null) return;

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length != 3 || requestParts[2] is not ("HTTP/1.0" or "HTTP/1.1"))
            {
                await WriteResponseAsync(stream, 400, "{}", token);
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var headerBytes = 0;
            var endedHeaders = false;
            for (var index = 0; index < 64; index++)
            {
                var line = await ReadLineAsync(stream, 4096, token);
                if (line is null) break;
                headerBytes += line.Length + 2;
                if (headerBytes > 16 * 1024)
                {
                    await WriteResponseAsync(stream, 431, "{}", token);
                    return;
                }
                if (line.Length == 0)
                {
                    endedHeaders = true;
                    break;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0 || !headers.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim()))
                {
                    await WriteResponseAsync(stream, 400, "{}", token);
                    return;
                }
            }

            if (!endedHeaders)
            {
                await WriteResponseAsync(stream, 431, "{}", token);
                return;
            }

            var status = 404;
            var body = "{\"error\":\"Not found\"}";
            var requestTarget = requestParts[1];
            if (requestParts[0] != "GET")
            {
                status = 405;
                body = "{\"error\":\"Only GET is supported\"}";
            }
            else if (requestTarget.StartsWith("//", StringComparison.Ordinal) || requestTarget.Contains('?') || requestTarget.Contains('#') || requestTarget.Contains('\\'))
            {
                status = 400;
                body = "{\"error\":\"Invalid fixture path\"}";
            }
            else if (!headers.TryGetValue("Host", out var host) || host != $"127.0.0.1:{Port}")
            {
                status = 421;
                body = "{\"error\":\"Unexpected host\"}";
            }
            else if (!headers.TryGetValue("X-BoundaryCheck-Fixture", out var guard) || !IsGuardValid(guard))
            {
                status = 403;
                body = "{\"error\":\"Fixture request rejected\"}";
            }
            else if (!headers.TryGetValue("X-BoundaryCheck-Identity", out var identity) || identity is not ("acme-user" or "birch-user"))
            {
                status = 401;
                body = "{\"error\":\"Unknown example identity\"}";
            }
            else if (requestTarget == "/api/documents/acme-plan" &&
                     (identity == "acme-user" || (_vulnerable && identity == "birch-user")))
            {
                status = 200;
                body = "{\"id\":\"sample-document\",\"title\":\"Example document\"}";
            }
            else if (requestTarget == "/api/documents/missing-doc")
            {
                status = 404;
                body = "{\"error\":\"Not found\"}";
            }

            await WriteResponseAsync(stream, status, body, token);
        }

        private bool IsGuardValid(string candidate)
        {
            var supplied = Encoding.ASCII.GetBytes(candidate);
            var expected = Encoding.ASCII.GetBytes(Guard);
            return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
        }

        private static async Task<string?> ReadLineAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var singleByte = new byte[1];
            while (true)
            {
                var count = await stream.ReadAsync(singleByte.AsMemory(), cancellationToken);
                if (count == 0)
                {
                    if (bytes.Count == 0) return null;
                    throw new IOException("Incomplete local fixture request.");
                }
                if (singleByte[0] == (byte)'\n')
                {
                    if (bytes.Count > 0 && bytes[^1] == (byte)'\r') bytes.RemoveAt(bytes.Count - 1);
                    return Encoding.ASCII.GetString(bytes.ToArray());
                }
                if (bytes.Count >= maxBytes) throw new InvalidDataException("Local fixture request line is too long.");
                bytes.Add(singleByte[0]);
            }
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken cancellationToken)
        {
            var reason = status switch
            {
                200 => "OK",
                400 => "Bad Request",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                421 => "Misdirected Request",
                431 => "Request Header Fields Too Large",
                _ => "Error",
            };
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {bodyBytes.Length}\r\n\r\n");
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }
}
