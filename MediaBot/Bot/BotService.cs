using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBot.Models;
using MediaBot.Services;
using Microsoft.Graph;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Resources;
using Microsoft.Skype.Bots.Media;

namespace MediaBot.Bot;

/// <summary>
/// ボット全体を管理するサービス（Singleton）。
///
/// 責務：
/// 1. Graph Communications クライアントの初期化
/// 2. 着信通話の受け付け
/// 3. CallHandler の作成・管理
/// </summary>
public class BotService
{
    private readonly AppSettings _settings;
    private readonly TranscriptionService _transcriptionService;
    private ICommunicationsClient? _client;

    private readonly Dictionary<string, CallHandler> _callHandlers = new();

    public BotService(AppSettings settings, TranscriptionService transcriptionService)
    {
        _settings = settings;
        _transcriptionService = transcriptionService;
    }

    /// <summary>
    /// Graph Communications クライアントを初期化する。
    ///
    /// CommunicationsClientBuilder は Teams との通信を全て管理する。
    /// シグナリング（誰がいるか、通話状態）と
    /// メディア（音声・映像）の両方を扱う。
    /// </summary>
    public async Task InitializeAsync()
    {
        // Azure AD トークン取得の設定（MSAL ベース）
        var authProvider = new AuthenticationProvider(
            _settings.AzureAdAppId,
            _settings.AzureAdAppSecret,
            _settings.AzureAdTenantId);

        // GraphLogger: SDK 組み込みのロガー
        // 引数: (componentName, obfuscators, redirectToTrace, obfuscationConfig)
        var logger = new GraphLogger("MediaBot", null, true, null);

        // CommunicationsClientBuilder は位置引数のみ（named params はコンパイル不可）
        // 引数順: (appId, applicationName, logger, ...)
        _client = new CommunicationsClientBuilder(
                _settings.AzureAdAppId,
                "Hackathon2026-MediaBot",
                logger)
            .SetAuthenticationProvider(authProvider)
            .SetNotificationUrl(new Uri(_settings.CallbackPath))
            .SetMediaPlatformSettings(new MediaPlatformSettings
            {
                // メディアプロセッサの公開エンドポイント（Windows VM の FQDN）
                MediaPlatformInstanceSettings = new MediaPlatformInstanceSettings
                {
                    CertificateThumbprint = "",
                    InstanceInternalPort = 8445,
                    InstancePublicIPAddress = IPAddress.Any,
                    InstancePublicPort = 13016,
                    ServiceFqdn = new Uri(_settings.BotBaseUrl).Host
                }
            })
            .Build();

        // 着信通話イベントのハンドラ登録
        _client.Calls().OnIncoming += OnIncomingCall;

        Console.WriteLine("✅ BotService 初期化完了");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 音声のみの MediaSession を作成するヘルパー。
    ///
    /// CreateMediaSession は video/vbss/data の null 型が 2 つのオーバーロードで
    /// 曖昧になるため、型付き変数で明示的に IEnumerable{VideoSocketSettings} を選択。
    /// </summary>
    private ILocalMediaSession CreateAudioOnlyMediaSession()
    {
        var audioSettings = new AudioSocketSettings
        {
            StreamDirections = StreamDirection.Recvonly,
            SupportedAudioFormat = AudioFormat.Pcm16K,
        };

        // 型付き null で IEnumerable<VideoSocketSettings> オーバーロードを選択
        IEnumerable<VideoSocketSettings>? noVideo = null;

        return _client!.CreateMediaSession(
            audioSettings,
            noVideo,           // videoSocketSettings (IEnumerable<VideoSocketSettings>)
            null,              // vbssSocketSettings (VideoSocketSettings)
            null,              // dataSocketSettings (DataSocketSettings)
            Guid.NewGuid());   // mediaSessionId
    }

    /// <summary>
    /// Teams から着信通話が来たときのハンドラ。
    /// AnswerAsync() で通話に参加し、同時に LocalMediaSession（音声の受け口）を設定する。
    /// </summary>
    private void OnIncomingCall(ICallCollection sender, CollectionEventArgs<ICall> args)
    {
        foreach (var call in args.AddedResources)
        {
            _ = Task.Run(async () =>
            {
                Console.WriteLine($"[着信] 通話ID: {call.Id}");

                var mediaSession = CreateAudioOnlyMediaSession();

                // AnswerAsync の引数: (IMediaSession, callbackUri, correlationId, CancellationToken, operationTimeout)
                await call.AnswerAsync(
                    mediaSession,
                    null,
                    Guid.NewGuid(),
                    CancellationToken.None,
                    0);

                var handler = new CallHandler(call, mediaSession, _transcriptionService);
                _callHandlers[call.Id] = handler;

                Console.WriteLine($"✅ 通話参加完了: {call.Id}");
            });
        }
    }

    /// <summary>
    /// 指定の Teams 会議に能動的に参加する（招待なしで入る）。
    /// スケジュール会議に自動参加するパターン。
    /// </summary>
    public async Task JoinMeetingAsync(string meetingUrl)
    {
        if (_client == null)
            throw new InvalidOperationException("BotService が初期化されていません");

        var mediaSession = CreateAudioOnlyMediaSession();

        // ICallCollection.AddAsync の引数: (Call, IMediaSession, Guid, CancellationToken)
        // MediaConfig は IMediaSession から SDK が内部で設定する
        var call = await _client.Calls().AddAsync(
            new Call
            {
                MeetingInfo = new OrganizerMeetingInfo
                {
                    Organizer = new IdentitySet(),
                    // TODO: meetingUrl から JoinWebUrl を解析して設定
                },
            },
            mediaSession,
            Guid.NewGuid(),
            CancellationToken.None);

        var handler = new CallHandler(call, mediaSession, _transcriptionService);
        _callHandlers[call.Id] = handler;

        Console.WriteLine($"✅ 会議参加完了: {call.Id}");
    }

    /// <summary>
    /// Teams からの HTTP 通知を SDK に渡して処理させる。
    /// Controller が受け取った Request をそのまま渡す。
    /// </summary>
    public async Task ProcessNotificationAsync(HttpRequestMessage request)
    {
        if (_client == null) return;
        await _client.ProcessNotificationAsync(request);
    }

    public IReadOnlyList<string> GetTranscript(string callId)
    {
        return _callHandlers.TryGetValue(callId, out var handler)
            ? handler.Transcript
            : Array.Empty<string>();
    }
}
