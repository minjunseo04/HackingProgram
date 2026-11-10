using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoundaryCheck;

internal sealed record LocalFixtureReport
{
    public int FormatVersion { get; init; } = 1;
    public string Product { get; init; } = "BoundaryCheck";
    public string AppVersion { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Mode { get; init; } = "";
    public string Scope { get; init; } = LocalFixtureReportStore.ScopeDescription;
    public string NotTested { get; init; } = LocalFixtureReportStore.NotTestedDescription;
    public string IntegrityNote { get; init; } = LocalFixtureReportStore.IntegrityDescription;
    public bool ResponseBodiesStored { get; init; }
    public List<LocalFixtureResult> Results { get; init; } = [];
}

internal static class LocalFixtureReportStore
{
    private const int MaximumFileSize = 1024 * 1024;
    private const int MaximumResultCount = 20;
    private static readonly HashSet<string> Modes = ["안전 예제", "취약 예제"];
    private static readonly HashSet<string> Verdicts = ["기대와 일치", "검토 필요", "판정 보류"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public const string ScopeDescription = "내장 Acme/Birch 가상 예제 · 127.0.0.1 임시 포트 · GET 요청만 · 프록시와 자동 리디렉션 사용 안 함";
    public const string NotTestedDescription = "사용자 프로젝트, 대상 주소, 가져온 HAR/OpenAPI 요청, 실제 계정, 외부 서버 및 다른 HTTP 방식은 검사하지 않았습니다. 이 결과는 전체 앱의 보안을 보증하지 않습니다.";
    public const string IntegrityDescription = "JSON 보고서는 사용자가 수정할 수 있습니다. 변조 방지 서명이나 독립적인 보증을 제공하지 않습니다.";

    public static LocalFixtureReport Create(bool vulnerable, IReadOnlyList<LocalFixtureResult> results) => new()
    {
        AppVersion = UpdateClient.CurrentVersion().ToString(),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        Mode = vulnerable ? "취약 예제" : "안전 예제",
        Results = results.ToList(),
    };

    public static void Save(string path, LocalFixtureReport report)
    {
        Validate(report);
        var content = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        if (content.Length > MaximumFileSize)
            throw new InvalidDataException("보고서가 허용된 파일 크기를 초과합니다.");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidDataException("보고서 저장 위치를 확인할 수 없습니다.");

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static LocalFixtureReport Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumFileSize)
            throw new InvalidDataException("보고서 파일이 비어 있거나 1MB 크기 제한을 초과합니다.");

        var content = new byte[(int)stream.Length];
        stream.ReadExactly(content);
        LocalFixtureReport? report;
        try
        {
            report = JsonSerializer.Deserialize<LocalFixtureReport>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("BoundaryCheck 보고서 JSON을 읽을 수 없습니다.", ex);
        }

        if (report is null) throw new InvalidDataException("보고서 내용이 비어 있습니다.");
        Validate(report);
        return report;
    }

    private static void Validate(LocalFixtureReport report)
    {
        if (report.FormatVersion != 1 || report.Product != "BoundaryCheck")
            throw new InvalidDataException("지원하지 않는 BoundaryCheck 보고서 형식입니다.");
        if (!Version.TryParse(report.AppVersion, out _) || report.AppVersion.Length > 24)
            throw new InvalidDataException("보고서 앱 버전 값이 올바르지 않습니다.");
        if (report.CreatedAtUtc == default || report.CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("보고서 생성 시각이 올바르지 않습니다.");
        if (report.Mode is null || !Modes.Contains(report.Mode))
            throw new InvalidDataException("보고서 실행 모드가 올바르지 않습니다.");
        if (report.Scope != ScopeDescription || report.NotTested != NotTestedDescription || report.IntegrityNote != IntegrityDescription || report.ResponseBodiesStored)
            throw new InvalidDataException("보고서 범위 또는 안내가 예상과 다릅니다.");
        if (report.Results is null || report.Results.Count is < 1 or > MaximumResultCount)
            throw new InvalidDataException("보고서 결과 수가 올바르지 않습니다.");

        foreach (var result in report.Results)
        {
            if (result is null ||
                !HasBoundedText(result.Scenario, 120) ||
                !HasBoundedText(result.Identity, 120) ||
                result.Method != "GET" ||
                !HasBoundedText(result.Path, 300) ||
                !result.Path.StartsWith("/api/", StringComparison.Ordinal) ||
                result.Path.Contains('?') || result.Path.Contains('#') || result.Path.Contains('\\') ||
                result.Expected is not ("허용" or "거부") ||
                !HasBoundedText(result.Actual, 32) ||
                !Verdicts.Contains(result.Verdict) ||
                !HasBoundedText(result.Note, 500))
            {
                throw new InvalidDataException("보고서에 허용되지 않는 검사 결과 항목이 있습니다.");
            }

            if (result.HttpStatusCode is int status)
            {
                if (status is < 100 or > 599 || result.Actual != status.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("보고서 HTTP 상태 코드가 올바르지 않습니다.");
            }
            else if (result.Actual != "응답 없음")
            {
                throw new InvalidDataException("HTTP 상태가 없는 결과에는 '응답 없음'으로 표시해야 합니다.");
            }

            if (result.ControlHttpStatusCode is int controlStatus && controlStatus is < 100 or > 599)
                throw new InvalidDataException("소유자 대조 HTTP 상태 코드가 올바르지 않습니다.");
        }
    }

    private static bool HasBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);
}
