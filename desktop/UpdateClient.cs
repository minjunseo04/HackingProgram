using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace BoundaryCheck;

internal sealed record ReleaseAsset(string Name, Uri Url, string? Digest);
internal sealed record ReleaseInfo(Version Version, string Tag, string Notes, ReleaseAsset Package);

internal static class UpdateClient
{
    private const string Repository = "minjunseo04/HackingProgram";
    private const string PackageName = "BoundaryCheck-win-x64.zip";
    private const long MaximumPackageBytes = 200L * 1024 * 1024;
    private const long MaximumExpandedBytes = 256L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BoundaryCheck/{CurrentVersion()}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Version CurrentVersion()
        => typeof(UpdateClient).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static async Task<ReleaseInfo?> FindLatestAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = $"https://api.github.com/repos/{Repository}/releases/latest";
        using var response = await Client.GetAsync(endpoint, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var versionMatch = System.Text.RegularExpressions.Regex.Match(tag, @"^v(?<version>\d+\.\d+\.\d+)$");
        if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups["version"].Value, out var version))
            throw new InvalidDataException("GitHub 릴리스 버전 태그가 v0.1.3 형식이 아닙니다.");

        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString() ?? "",
                new Uri(asset.GetProperty("browser_download_url").GetString() ?? ""),
                asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null))
            .ToDictionary(asset => asset.Name, StringComparer.OrdinalIgnoreCase);
        if (!assets.TryGetValue(PackageName, out var package))
            throw new InvalidDataException("이 릴리스에 Windows 앱 ZIP 파일이 없습니다.");
        ValidateReleaseAsset(package.Url);
        if (package.Digest is null || !System.Text.RegularExpressions.Regex.IsMatch(package.Digest, @"^sha256:[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("GitHub가 앱 파일의 SHA-256 값을 제공하지 않았습니다.");
        var notes = root.TryGetProperty("body", out var bodyText) ? bodyText.GetString() ?? "" : "";
        return new ReleaseInfo(version, tag, notes, package);
    }

    public static async Task<string> DownloadVerifiedPackageAsync(ReleaseInfo release, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var expectedHash = release.Package.Digest!["sha256:".Length..].ToUpperInvariant();

        using var response = await Client.GetAsync(release.Package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
            throw new InvalidDataException("업데이트 파일이 허용 크기(200 MiB)를 초과합니다.");

        var updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoundaryCheck", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var archivePath = Path.Combine(updateDirectory, $"BoundaryCheck-{release.Tag}.zip");
        try
        {
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[65536];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                total += count;
                if (total > MaximumPackageBytes) throw new InvalidDataException("업데이트 파일이 허용 크기(200 MiB)를 초과합니다.");
                hash.AppendData(buffer, 0, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > 0)
                    progress?.Report((int)Math.Min(100, total * 100 / contentLength.Value));
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
            {
                File.Delete(archivePath);
                throw new InvalidDataException("다운로드한 업데이트 파일의 SHA-256 값이 일치하지 않습니다.");
            }
        }

        ValidatePackageContents(archivePath);
        return archivePath;
        }
        catch
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
            throw;
        }
    }

    private static void ValidateReleaseAsset(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("릴리스 파일 주소가 지정한 GitHub 저장소와 일치하지 않습니다.");
    }

    private static void ValidatePackageContents(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        if (archive.Entries.Sum(entry => entry.Length) > MaximumExpandedBytes)
            throw new InvalidDataException("업데이트 ZIP의 압축 해제 크기가 허용량을 초과합니다.");
        if (names.Any(name => name.Contains('/') || name.Contains('\\') || name is "." or ".."))
            throw new InvalidDataException("업데이트 ZIP에 허용되지 않은 경로가 있습니다.");
        var allowed = new HashSet<string>(["BoundaryCheck.exe", "apply-update.ps1", "README.txt"], StringComparer.OrdinalIgnoreCase);
        if (names.Length != allowed.Count || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length || names.Any(name => !allowed.Contains(name)))
            throw new InvalidDataException("업데이트 ZIP에 허용되지 않은 파일이 있습니다.");
        if (!names.Contains("BoundaryCheck.exe", StringComparer.OrdinalIgnoreCase)
            || !names.Contains("apply-update.ps1", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("업데이트 ZIP에 앱 또는 업데이트 도우미가 없습니다.");
    }
}
