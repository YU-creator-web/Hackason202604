using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace MediaBot.Services;

/// <summary>
/// STT → GPT-4o → TTS の完全な会話ループテスト。
/// Teams なしで、マイクとスピーカーだけで双方向会話を再現する。
///
/// 【本番の Teams ボットとの違い】
/// - 音声入力: マイク（本番は Teams の PushAudioInputStream）
/// - 音声出力: スピーカー（本番は Teams への AudioSocket 送信 ※要 Sendonly 設定）
/// - 会話処理: GPT-4o ← これは本番でも全く同じコード
/// </summary>
public static class ConversationTest
{
    private static readonly HttpClient _httpClient = new();

    public static async Task RunAsync(
        string speechKey, string speechRegion,
        string openAiKey, string openAiEndpoint, string openAiDeployment)
    {
        Console.WriteLine();
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("🤖 双方向会話テスト (STT → GPT-4o → TTS)");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("話しかけると AI が音声で返答します。");
        Console.WriteLine("Ctrl+C で終了。");
        Console.WriteLine();

        var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        speechConfig.SpeechRecognitionLanguage = "ja-JP";
        // ja-JP-NanamiNeural: 自然な日本語女性音声（Azure Neural Voice）
        speechConfig.SpeechSynthesisVoiceName = "ja-JP-NanamiNeural";

        // 会話履歴（GPT-4o はこれを見て文脈を理解する）
        var history = new List<object>
        {
            new { role = "system", content =
                "あなたは Teams 会議に参加している AI アシスタントです。" +
                "会議の参加者と自然な会話をしてください。" +
                "返答は簡潔に、2〜3文以内でお願いします。" }
        };

        while (true)
        {
            // ── STEP 1: STT（マイクで発話を認識）──────────────────
            Console.WriteLine("[あなた] 話してください...");

            string userText;
            using (var audioConfig = AudioConfig.FromDefaultMicrophoneInput())
            using (var recognizer = new SpeechRecognizer(speechConfig, audioConfig))
            {
                // RecognizeOnceAsync: 1発話が終わると自動で停止（ターン制に最適）
                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.Canceled)
                {
                    var detail = CancellationDetails.FromResult(result);
                    Console.WriteLine($"[エラー] STT 失敗: {detail.ErrorDetails}");
                    break;
                }

                if (result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrWhiteSpace(result.Text))
                {
                    Console.WriteLine("[スキップ] 音声を認識できませんでした。もう一度話してください。");
                    continue;
                }

                userText = result.Text;
                Console.WriteLine($"[あなた] {userText}");
            }

            // ── STEP 2: GPT-4o（テキストで応答生成）──────────────
            history.Add(new { role = "user", content = userText });

            Console.Write("[ボット] 考え中...");
            var botText = await CallGptAsync(openAiKey, openAiEndpoint, openAiDeployment, history);

            history.Add(new { role = "assistant", content = botText });
            Console.WriteLine($"\r[ボット] {botText}");

            // ── STEP 3: TTS（テキストを音声で読み上げ）────────────
            using var synthesizer = new SpeechSynthesizer(
                speechConfig,
                AudioConfig.FromDefaultSpeakerOutput());

            await synthesizer.SpeakTextAsync(botText);
        }
    }

    /// <summary>
    /// Azure OpenAI の Chat Completions API を呼ぶ。
    /// 追加 NuGet なし、HttpClient の REST 呼び出しのみ。
    /// </summary>
    private static async Task<string> CallGptAsync(
        string apiKey, string endpoint, string deployment,
        List<object> history)
    {
        var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version=2024-02-01";

        var body = JsonSerializer.Serialize(new
        {
            messages = history,
            max_tokens = 300,
            temperature = 0.7
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"\n[GPT エラー] {response.StatusCode}: {responseBody}");
            return "すみません、エラーが発生しました。";
        }

        // choices[0].message.content を取り出す
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "（応答なし）";
    }
}
