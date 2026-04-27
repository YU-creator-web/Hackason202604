using System;
using System.Threading.Tasks;
using MediaBot.Models;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace MediaBot.Services;

/// <summary>
/// Azure Speech Services を使ってリアルタイム音声→テキスト変換を行うサービス。
///
/// 仕組み：
/// Teams から届く音声バッファ（PCM 16kHz 16bit mono）を
/// Azure Speech SDK の PushAudioInputStream に流し込み、
/// 認識結果をコールバックで受け取る。
/// </summary>
public class TranscriptionService
{
    private readonly SpeechConfig _speechConfig;

    public TranscriptionService(AppSettings settings)
    {
        _speechConfig = SpeechConfig.FromSubscription(settings.SpeechKey, settings.SpeechRegion);
        _speechConfig.SpeechRecognitionLanguage = "ja-JP";
    }

    /// <summary>
    /// 通話ごとのリアルタイム文字起こしセッションを開始する。
    /// 戻り値の (stream, recognizer) を CallHandler が保持し、
    /// 音声バッファが届くたびに stream.Write() を呼ぶ。
    /// </summary>
    public (PushAudioInputStream stream, SpeechRecognizer recognizer) CreateSession(
        string participantId,
        Func<string, string, Task> onTranscribed)
    {
        // PushAudioInputStream: 外部からPCMデータを「プッシュ」できる入力ストリーム
        // Teams の音声バッファをそのまま流し込める
        var audioFormat = AudioStreamFormat.GetWaveFormatPCM(
            samplesPerSecond: 16000,  // 16kHz
            bitsPerSample: 16,         // 16bit
            channels: 1                // モノラル
        );
        var audioStream = AudioInputStream.CreatePushStream(audioFormat);
        var audioConfig = AudioConfig.FromStreamInput(audioStream);

        var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

        // 認識中（まだ途中）のテキストが届くイベント
        recognizer.Recognizing += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
                Console.WriteLine($"[認識中] {participantId}: {e.Result.Text}");
        };

        // 確定したテキストが届くイベント
        recognizer.Recognized += async (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
            {
                Console.WriteLine($"[確定] {participantId}: {e.Result.Text}");
                await onTranscribed(participantId, e.Result.Text);
            }
        };

        recognizer.StartContinuousRecognitionAsync().GetAwaiter().GetResult();

        return ((PushAudioInputStream)audioStream, recognizer);
    }
}
