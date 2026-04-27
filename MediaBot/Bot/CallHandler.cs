using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MediaBot.Services;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Resources;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Skype.Bots.Media;

namespace MediaBot.Bot;

/// <summary>
/// 1つの Teams 通話を管理するクラス。
///
/// Application Hosted Media Bot の核心部分。
/// Teams から届くリアルタイム音声バッファを受け取り、
/// Azure Speech SDK に流して文字起こしする。
///
/// 音声の流れ：
/// Teams ──(SRTP/UDP)──→ Media SDK ──→ AudioSocket ──→ このクラス ──→ Speech SDK
///
/// 【混合音声モードについて】
/// AudioSocket はデフォルトで全参加者の音声を混合した PCM データを返す。
/// 話者分離（誰が話したか）が必要な場合は UnmixedAudioBuffers を使うが、
/// そのためには会議側での設定も必要になる。
/// ここでは学習目的でシンプルな混合音声モードを使用。
/// </summary>
public class CallHandler : IDisposable
{
    private readonly ICall _call;
    private readonly IAudioSocket _audioSocket;
    private readonly TranscriptionService _transcriptionService;

    // 混合音声用のシングル文字起こしセッション
    private (PushAudioInputStream stream, IDisposable recognizer)? _session;
    private readonly List<string> _transcript = new();
    private bool _sessionInitialized = false;

    public string CallId => _call.Id;
    public IReadOnlyList<string> Transcript => _transcript.AsReadOnly();

    public CallHandler(ICall call, ILocalMediaSession mediaSession, TranscriptionService transcriptionService)
    {
        _call = call;
        _transcriptionService = transcriptionService;

        // AudioSocket: Teams から届くリアルタイム音声の受信口
        _audioSocket = mediaSession.AudioSocket;

        // 音声バッファが届くたびに OnAudioMediaReceived が呼ばれる
        // 20ms ごとに約 640 バイトの PCM データが届く（16kHz × 16bit × 20ms）
        _audioSocket.AudioMediaReceived += OnAudioMediaReceived;

        // 参加者の変化を監視
        _call.Participants.OnUpdated += OnParticipantsUpdated;

        Console.WriteLine($"[CallHandler] 通話開始: {CallId}");
    }

    /// <summary>
    /// 音声バッファ受信ハンドラ。
    /// Teams から 20ms ごとに呼ばれる（= 毎秒 50 回）。
    /// AudioMediaBuffer に生の PCM データが入っている。
    /// </summary>
    private void OnAudioMediaReceived(object? sender, AudioMediaReceivedEventArgs args)
    {
        try
        {
            var buffer = args.Buffer;

            // 最初の音声受信時にのみ Speech SDK セッションを初期化
            if (!_sessionInitialized)
            {
                var (stream, recognizer) = _transcriptionService.CreateSession(
                    "mixed",
                    onTranscribed: (id, text) =>
                    {
                        var entry = $"[文字起こし] {text}";
                        _transcript.Add(entry);
                        Console.WriteLine(entry);
                        // TODO: GPT-4o に送って応答生成
                        return Task.CompletedTask;
                    });

                _session = (stream, recognizer);
                _sessionInitialized = true;
            }

            // PCM バイト列を Speech SDK に流し込む
            // Marshal.Copy: アンマネージドメモリ（buffer.Data）→ マネージド byte[]
            // unsafe ブロック不要（IntPtr オーバーロードを使用）
            var data = new byte[buffer.Length];
            Marshal.Copy(buffer.Data, data, 0, (int)buffer.Length);
            _session?.stream.Write(data);
        }
        finally
        {
            // Media SDK のバッファは必ず Dispose する（アンマネージドメモリなので GC が回収しない）
            args.Buffer.Dispose();
        }
    }

    private void OnParticipantsUpdated(IParticipantCollection sender, CollectionEventArgs<IParticipant> args)
    {
        foreach (var participant in args.AddedResources)
            Console.WriteLine($"[参加] {participant.Resource.Info.Identity?.User?.DisplayName}");

        foreach (var participant in args.RemovedResources)
            Console.WriteLine($"[退出] {participant.Resource.Info.Identity?.User?.DisplayName}");
    }

    public void Dispose()
    {
        _audioSocket.AudioMediaReceived -= OnAudioMediaReceived;

        _session?.stream.Dispose();
        _session?.recognizer.Dispose();
    }
}
