using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace MediaBot.Services;

/// <summary>
/// Azure Speech SDK の動作確認テスト。
/// Teams なしで、マイクまたは WAV ファイルから文字起こしを試す。
///
/// 【テストの意味】
/// Teams から届く音声は PCM 16kHz 16bit mono の byte[]。
/// このテストでは同じ形式の音声（マイクや WAV ファイル）を使って
/// Speech SDK が正しく文字起こしできるか確認する。
/// </summary>
public static class SpeechTest
{
    /// <summary>
    /// モード A: マイク入力で連続認識テスト。
    /// 実際に声を出して、リアルタイムで文字起こしされることを確認する。
    /// </summary>
    public static async Task RunMicrophoneTestAsync(string speechKey, string speechRegion)
    {
        Console.WriteLine();
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("🎤 モード A: マイクテスト");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("マイクに向かって日本語で話してください。");
        Console.WriteLine("Enter を押すと停止します。");
        Console.WriteLine();

        var config = SpeechConfig.FromSubscription(speechKey, speechRegion);
        config.SpeechRecognitionLanguage = "ja-JP";

        // マイク入力（Teams の代わり）
        using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
        using var recognizer = new SpeechRecognizer(config, audioConfig);

        // 認識中（途中結果）
        recognizer.Recognizing += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
                Console.Write($"\r[認識中] {e.Result.Text,-50}");
        };

        // 確定テキスト（TranscriptionService の Recognized イベントと同じ）
        recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
            {
                Console.WriteLine();
                Console.WriteLine($"[確定 ✅] {e.Result.Text}");
            }
        };

        recognizer.Canceled += (_, e) =>
        {
            Console.WriteLine();
            Console.WriteLine($"[エラー] {e.Reason}: {e.ErrorDetails}");
        };

        await recognizer.StartContinuousRecognitionAsync();

        // PowerShell の Enter キーが流れ込まないよう先にフラッシュ
        while (Console.KeyAvailable) Console.ReadKey(true);
        Console.ReadLine();

        await recognizer.StopContinuousRecognitionAsync();

        Console.WriteLine("✅ マイクテスト完了");
    }

    /// <summary>
    /// モード B: WAV ファイルを PushAudioInputStream に流すテスト。
    /// Teams からの音声パス（CallHandler の OnAudioMediaReceived）を完全再現する。
    ///
    /// WAV ファイルは 16kHz / 16bit / モノラル 形式が必要。
    /// 変換コマンド: ffmpeg -i input.mp3 -ar 16000 -ac 1 -f s16le output.raw
    /// </summary>
    public static async Task RunWavFileTestAsync(string speechKey, string speechRegion, string wavPath)
    {
        Console.WriteLine();
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📁 モード B: WAV ファイルテスト");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"ファイル: {wavPath}");
        Console.WriteLine();

        var config = SpeechConfig.FromSubscription(speechKey, speechRegion);
        config.SpeechRecognitionLanguage = "ja-JP";

        // PushAudioInputStream: Teams の音声パスを再現
        // CallHandler は Teams から 640 バイトのチャンクを受け取るたびに Write() を呼ぶ
        var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        var pushStream = (PushAudioInputStream)AudioInputStream.CreatePushStream(audioFormat);

        using var audioConfig = AudioConfig.FromStreamInput(pushStream);
        using var recognizer = new SpeechRecognizer(config, audioConfig);

        var tcs = new TaskCompletionSource<bool>();

        recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                Console.WriteLine($"[確定 ✅] {e.Result.Text}");
        };

        recognizer.SessionStopped += (_, _) => tcs.TrySetResult(true);
        recognizer.Canceled += (_, e) =>
        {
            Console.WriteLine($"[エラー] {e.Reason}: {e.ErrorDetails}");
            tcs.TrySetResult(false);
        };

        await recognizer.StartContinuousRecognitionAsync();

        // WAV ファイルを読んでチャンク送信（Teams の 20ms パケットを再現）
        var bytes = File.ReadAllBytes(wavPath);
        int headerSize = 44; // WAV ヘッダーをスキップ
        int chunkSize = 640; // 20ms @ 16kHz 16bit mono = 640 bytes（Teams と同じ）

        for (int i = headerSize; i < bytes.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, bytes.Length - i);
            var chunk = new byte[length];
            Array.Copy(bytes, i, chunk, 0, length);
            pushStream.Write(chunk);
            await Task.Delay(20); // 20ms ごとに送信（実際の Teams と同じリズム）
        }

        pushStream.Close(); // ストリーム終端を通知
        await Task.WhenAny(tcs.Task, Task.Delay(10000)); // 最大10秒待つ

        await recognizer.StopContinuousRecognitionAsync();
        Console.WriteLine("✅ WAV ファイルテスト完了");
    }
}
