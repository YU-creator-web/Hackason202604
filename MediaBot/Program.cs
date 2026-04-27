using MediaBot.Bot;
using MediaBot.Models;
using MediaBot.Services;
using Microsoft.Owin.Hosting;

internal class Program
{
    static async Task Main(string[] args)
    {
        var settings = AppSettingsLoader.Load("appsettings.json");

        // --chat-test      : STT → GPT-4o → TTS の双方向会話テスト
        if (args.Length > 0 && args[0] == "--chat-test")
        {
            if (settings.AzureOpenAiKey.StartsWith("（") || settings.SpeechKey.StartsWith("（"))
            {
                Console.WriteLine("❌ appsettings.json の SpeechKey または AzureOpenAiKey が設定されていません。");
                return;
            }
            await ConversationTest.RunAsync(
                settings.SpeechKey, settings.SpeechRegion,
                settings.AzureOpenAiKey, settings.AzureOpenAiEndpoint, settings.AzureOpenAiDeployment);
            return;
        }

        // --speech-test    : マイクで Speech SDK をテスト
        // --speech-test <wavファイルパス> : WAV ファイルで Speech SDK をテスト
        if (args.Length > 0 && args[0] == "--speech-test")
        {
            if (string.IsNullOrEmpty(settings.SpeechKey) || settings.SpeechKey.StartsWith("（"))
            {
                Console.WriteLine("❌ appsettings.json の SpeechKey が設定されていません。");
                Console.WriteLine("   Azure Portal → Speech Services → Keys and Endpoint でキーを取得してください。");
                return;
            }

            if (args.Length > 1 && File.Exists(args[1]))
            {
                // モード B: WAV ファイルテスト
                await SpeechTest.RunWavFileTestAsync(settings.SpeechKey, settings.SpeechRegion, args[1]);
            }
            else
            {
                // モード A: マイクテスト
                await SpeechTest.RunMicrophoneTestAsync(settings.SpeechKey, settings.SpeechRegion);
            }
            return;
        }

        // 通常起動: Media Bot サーバー
        var transcriptionService = new TranscriptionService(settings);
        var botService = new BotService(settings, transcriptionService);

        string baseUrl = "https://+:443/";

        Console.WriteLine("🤖 MediaBot 起動中...");

        Startup.BotService = botService;
        Startup.Settings = settings;

        await botService.InitializeAsync();

        using (WebApp.Start<Startup>(baseUrl))
        {
            Console.WriteLine("✅ MediaBot 起動完了");
            Console.WriteLine($"   Webhook URL: {settings.CallbackPath}");
            Console.WriteLine("   Ctrl+C で停止");
            Console.ReadLine();
        }
    }
}
