using System.Text.Json;

namespace BoundaryCheck;

internal sealed record LocalFixtureWriteSettingsData
{
    public bool AllowLoopbackWriteTests { get; init; }
}

internal static class LocalFixtureWriteSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BoundaryCheck",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8 };

    public static bool IsEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 4096) return false;
            var settings = JsonSerializer.Deserialize<LocalFixtureWriteSettingsData>(stream, JsonOptions);
            return settings?.AllowLoopbackWriteTests == true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(bool enabled)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(
            new LocalFixtureWriteSettingsData { AllowLoopbackWriteTests = enabled },
            JsonOptions);
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("설정 저장 위치를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".settings.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
