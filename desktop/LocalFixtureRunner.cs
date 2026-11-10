using System.Net;
using System.Net.Http.Headers;
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
    private const string WritePath = "/api/documents/acme-plan/label";
    private const string WriteStatePath = "/api/documents/acme-plan/label-check/owner";
    private const string OwnerLabel = "owner-write-marker";
    private const string BirchLabel = "birch-write-marker";

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

    public static async Task<IReadOnlyList<LocalFixtureResult>> RunWriteAsync(bool sessionConsent, CancellationToken cancellationToken)
    {
        if (!LocalFixtureWriteSettings.IsEnabled() || !sessionConsent)
            throw new InvalidOperationException("앱 설정과 이번 실행의 동의가 모두 필요합니다.");

        var results = new List<LocalFixtureResult>(6);
        results.AddRange(await RunWriteModeAsync(vulnerable: false, cancellationToken));
        results.AddRange(await RunWriteModeAsync(vulnerable: true, cancellationToken));
        return results;
    }

    private static async Task<IReadOnlyList<LocalFixtureResult>> RunWriteModeAsync(bool vulnerable, CancellationToken cancellationToken)
    {
        await using var fixture = new LoopbackFixtureServer(vulnerable);
        fixture.Start();

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var results = new List<LocalFixtureResult>(3);
        var mode = vulnerable ? "취약 예제" : "안전 예제";
        var ownerStatus = await TrySendPatchAsync(client, fixture, "acme-user", OwnerLabel, cancellationToken);
        var ownerVerdict = Evaluate("허용", null, ownerStatus);
        results.Add(CreateWriteResult(
            $"{mode} · 소유자 라벨 변경",
            "Acme 일반 사용자",
            "PATCH",
            WritePath,
            "허용",
            ownerStatus,
            null,
            ownerVerdict.Verdict,
            ownerStatus is >= 200 and < 300
                ? "허용된 계정이 가상 문서 라벨을 바꿨습니다. 메모리 데이터는 실행 종료 후 폐기됩니다."
                : ownerVerdict.Note));

        int? crossTenantStatus = null;
        int? stateStatus = null;
        if (ownerStatus is >= 200 and < 300)
        {
            crossTenantStatus = await TrySendPatchAsync(client, fixture, "birch-user", BirchLabel, cancellationToken);
            var crossTenantVerdict = Evaluate("거부", ownerStatus, crossTenantStatus);
            var crossTenantNote = crossTenantStatus is >= 200 and < 300
                ? "다른 조직 요청이 성공해 예제 메모리의 문서 라벨을 바꿨습니다. 실제 서비스 데이터는 사용하지 않았습니다."
                : crossTenantVerdict.Note;
            results.Add(CreateWriteResult(
                $"{mode} · 다른 조직 라벨 변경",
                "Birch 일반 사용자",
                "PATCH",
                WritePath,
                "거부",
                crossTenantStatus,
                ownerStatus,
                crossTenantVerdict.Verdict,
                crossTenantNote));

            stateStatus = await TrySendGetAsync(client, fixture, "acme-user", WriteStatePath, cancellationToken);
        }
        else
        {
            results.Add(CreateWriteResult(
                $"{mode} · 다른 조직 라벨 변경",
                "Birch 일반 사용자",
                "PATCH",
                WritePath,
                "거부",
                null,
                ownerStatus,
                "판정 보류",
                "소유자 쓰기 대조가 성공하지 않아 다른 조직 쓰기를 실행하지 않았습니다."));
        }

        var stateVerdict = stateStatus switch
        {
            204 => ("기대와 일치", "다른 조직 요청 이후에도 소유자 전용 라벨이 유지됐습니다."),
            409 => ("검토 필요", "다른 조직 요청 이후 소유자 전용 라벨이 바뀌었습니다."),
            _ => Evaluate("허용", ownerStatus, stateStatus),
        };
        results.Add(CreateWriteResult(
            $"{mode} · 소유자 라벨 유지 확인",
            "Acme 일반 사용자",
            "GET",
            WriteStatePath,
            "허용",
            stateStatus,
            ownerStatus,
            stateVerdict.Item1,
            stateVerdict.Item2));

        return results;
    }

    private static LocalFixtureResult CreateWriteResult(
        string scenario,
        string identity,
        string method,
        string path,
        string expected,
        int? status,
        int? controlStatus,
        string verdict,
        string note) => new(
            scenario,
            identity,
            method,
            path,
            expected,
            status?.ToString(CultureInfo.InvariantCulture) ?? "응답 없음",
            status,
            controlStatus,
            verdict,
            note);

    private static async Task<int?> TrySendPatchAsync(
        HttpClient client,
        LoopbackFixtureServer fixture,
        string identityId,
        string fixedLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendPatchAsync(client, fixture, identityId, fixedLabel, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static async Task<int?> TrySendGetAsync(
        HttpClient client,
        LoopbackFixtureServer fixture,
        string identityId,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendGetAsync(client, fixture, identityId, path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static async Task<int> SendPatchAsync(
        HttpClient client,
        LoopbackFixtureServer fixture,
        string identityId,
        string fixedLabel,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://127.0.0.1:{fixture.Port}{WritePath}", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri);
        request.Headers.Add("X-BoundaryCheck-Fixture", fixture.Guard);
        request.Headers.Add("X-BoundaryCheck-Identity", identityId);
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes($"{{\"label\":\"{fixedLabel}\"}}"));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return (int)response.StatusCode;
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

    private static (string Verdict, string Note) Evaluate(string expected, int? controlStatus, int? actualStatus)
    {
        if (expected == "허용")
        {
            if (actualStatus is >= 200 and < 300)
                return ("기대와 일치", "허용된 예제 요청이 성공 응답을 받았습니다.");
            if (actualStatus is null)
                return ("판정 보류", "요청을 완료하지 못해 결과를 판정할 수 없습니다.");
            if (actualStatus >= 500)
                return ("판정 보류", "예제 서버 오류만으로 접근 규칙을 판정할 수 없습니다.");
            return ("검토 필요", "허용을 기대했지만 성공 응답을 받지 못했습니다.");
        }

        if (controlStatus is null)
            return ("판정 보류", "소유자 대조 요청이 없어 자료 존재 여부를 확인하지 못했습니다.");
        if (controlStatus == 404)
            return ("판정 보류", "소유자 대조 요청도 404여서 자료가 존재하는지 확인할 수 없습니다.");
        if (controlStatus is < 200 or >= 300)
            return ("판정 보류", "소유자 대조 요청이 성공하지 않아 차단 결과를 비교할 수 없습니다.");
        if (actualStatus is null)
            return ("판정 보류", "요청을 완료하지 못해 차단 결과를 판정할 수 없습니다.");

        if (actualStatus is 401 or 403 or 404)
            return ("기대와 일치", "소유자 대조는 성공했고 요청 신원은 차단 응답을 받았습니다.");
        if (actualStatus is >= 200 and < 300)
            return ("검토 필요", "거부를 기대한 다른 조직 요청이 성공했습니다.");
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
        private string _documentLabel = "initial-label";
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

            if (!headers.TryGetValue("Host", out var host) || host != $"127.0.0.1:{Port}")
            {
                await WriteResponseAsync(stream, 421, "{}", token);
                return;
            }
            if (!headers.TryGetValue("X-BoundaryCheck-Fixture", out var guard) || !IsGuardValid(guard))
            {
                await WriteResponseAsync(stream, 403, "{}", token);
                return;
            }
            if (!headers.TryGetValue("X-BoundaryCheck-Identity", out var identity) || identity is not ("acme-user" or "birch-user"))
            {
                await WriteResponseAsync(stream, 401, "{}", token);
                return;
            }

            var status = 404;
            var body = "{\"error\":\"Not found\"}";
            var requestTarget = requestParts[1];
            var requestBody = Array.Empty<byte>();
            if (headers.ContainsKey("Transfer-Encoding"))
            {
                await WriteResponseAsync(stream, 400, "{}", token);
                return;
            }
            if (requestParts[0] == "PATCH")
            {
                if (!headers.TryGetValue("Content-Length", out var lengthText) ||
                    !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength) ||
                    contentLength <= 0)
                {
                    await WriteResponseAsync(stream, 400, "{}", token);
                    return;
                }
                if (contentLength > 256)
                {
                    await WriteResponseAsync(stream, 413, "{}", token);
                    return;
                }
                if (!headers.TryGetValue("Content-Type", out var contentType) ||
                    !contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 415, "{}", token);
                    return;
                }
                requestBody = new byte[contentLength];
                try { await stream.ReadExactlyAsync(requestBody.AsMemory(), token); }
                catch (EndOfStreamException)
                {
                    await WriteResponseAsync(stream, 400, "{}", token);
                    return;
                }
            }
            else if (headers.TryGetValue("Content-Length", out var getLength) && getLength != "0")
            {
                await WriteResponseAsync(stream, 400, "{}", token);
                return;
            }

            if (requestParts[0] is not ("GET" or "PATCH"))
            {
                status = 405;
                body = "{\"error\":\"Only GET and the fixed PATCH example are supported\"}";
            }
            else if (requestTarget.StartsWith("//", StringComparison.Ordinal) || requestTarget.Contains('?') || requestTarget.Contains('#') || requestTarget.Contains('\\'))
            {
                status = 400;
                body = "{\"error\":\"Invalid fixture path\"}";
            }
            else if (requestParts[0] == "GET" && requestTarget == "/api/documents/acme-plan/label-check/owner")
            {
                if (identity != "acme-user")
                {
                    status = 403;
                    body = "{\"error\":\"Only the owner can verify the example label\"}";
                }
                else if (_documentLabel == OwnerLabel)
                {
                    status = 204;
                    body = "";
                }
                else
                {
                    status = 409;
                    body = "{\"error\":\"The owner label changed\"}";
                }
            }
            else if (requestParts[0] == "GET" && requestTarget == "/api/documents/acme-plan" &&
                     (identity == "acme-user" || (_vulnerable && identity == "birch-user")))
            {
                status = 200;
                body = "{\"id\":\"sample-document\",\"title\":\"Example document\"}";
            }
            else if (requestParts[0] == "GET" && requestTarget == "/api/documents/missing-doc")
            {
                status = 404;
                body = "{\"error\":\"Not found\"}";
            }
            else if (requestParts[0] == "PATCH" && requestTarget == WritePath)
            {
                var label = Encoding.UTF8.GetString(requestBody);
                var allowedPayload = identity switch
                {
                    "acme-user" => label == $"{{\"label\":\"{OwnerLabel}\"}}",
                    "birch-user" => label == $"{{\"label\":\"{BirchLabel}\"}}",
                    _ => false,
                };
                if (!allowedPayload)
                {
                    status = 400;
                    body = "{\"error\":\"Unexpected fixture payload\"}";
                }
                else if (identity == "acme-user" || _vulnerable)
                {
                    _documentLabel = identity == "acme-user" ? OwnerLabel : BirchLabel;
                    status = 204;
                    body = "";
                }
                else
                {
                    status = 403;
                    body = "{\"error\":\"Cross-organization write rejected\"}";
                }
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
                204 => "No Content",
                400 => "Bad Request",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                409 => "Conflict",
                421 => "Misdirected Request",
                431 => "Request Header Fields Too Large",
                413 => "Payload Too Large",
                415 => "Unsupported Media Type",
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
