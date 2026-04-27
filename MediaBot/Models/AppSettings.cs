using System.IO;
using System.Text.Json;

namespace MediaBot.Models;

public class AppSettings
{
    public string AzureAdTenantId { get; set; } = "";
    public string AzureAdAppId { get; set; } = "";
    public string AzureAdAppSecret { get; set; } = "";
    public string BotBaseUrl { get; set; } = "";
    public string SpeechKey { get; set; } = "";
    public string SpeechRegion { get; set; } = "japaneast";
    public string AzureOpenAiKey { get; set; } = "";
    public string AzureOpenAiEndpoint { get; set; } = "";
    public string AzureOpenAiDeployment { get; set; } = "gpt-4o";

    public string CallbackPath => $"{BotBaseUrl}/api/calling/notification";
}

/// <summary>
/// appsettings.json を読み込むユーティリティ。
/// .NET Framework には Configuration の組み込みサポートがないため手動で読む。
/// </summary>
public static class AppSettingsLoader
{
    public static AppSettings Load(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("AppSettings");
        return JsonSerializer.Deserialize<AppSettings>(section.GetRawText())
            ?? throw new InvalidOperationException("AppSettings が読み込めませんでした");
    }
}
