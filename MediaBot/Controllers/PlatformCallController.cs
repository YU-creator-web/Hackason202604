using MediaBot.Bot;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace MediaBot.Controllers;

/// <summary>
/// Teams（Microsoft Graph）からの Webhook を受け取るコントローラー。
/// .NET Framework WebAPI では ApiController を継承する。
/// （ASP.NET Core の ControllerBase に相当）
/// </summary>
[RoutePrefix("api/calling")]
public class PlatformCallController : ApiController
{
    private readonly BotService _botService;

    public PlatformCallController(BotService botService)
    {
        _botService = botService;
    }

    /// <summary>
    /// Teams からの通話通知を受け取るエンドポイント。
    /// POST https://（ボットのURL）/api/calling/notification
    ///
    /// Azure AD アプリ登録の "Calling Webhook URL" に設定する URL がここ。
    /// </summary>
    [HttpPost]
    [Route("notification")]
    public async Task<HttpResponseMessage> OnNotificationAsync()
    {
        // SDK が通知を解析してイベントを発火させる
        await _botService.ProcessNotificationAsync(Request);
        return Request.CreateResponse(HttpStatusCode.OK);
    }

    /// <summary>
    /// 特定の Teams 会議に能動参加する（テスト・管理用）。
    /// POST https://（ボットのURL）/api/calling/join
    /// Body: { "meetingUrl": "https://teams.microsoft.com/l/meetup-join/..." }
    /// </summary>
    [HttpPost]
    [Route("join")]
    public async Task<HttpResponseMessage> JoinMeetingAsync([FromBody] JoinRequest request)
    {
        await _botService.JoinMeetingAsync(request.MeetingUrl);
        return Request.CreateResponse(HttpStatusCode.OK, new { message = "会議参加処理を開始しました" });
    }

    /// <summary>
    /// 文字起こし結果を取得する。
    /// GET https://（ボットのURL）/api/calling/transcript/{callId}
    /// </summary>
    [HttpGet]
    [Route("transcript/{callId}")]
    public HttpResponseMessage GetTranscript(string callId)
    {
        var transcript = _botService.GetTranscript(callId);
        return Request.CreateResponse(HttpStatusCode.OK, transcript);
    }
}

public class JoinRequest
{
    public string MeetingUrl { get; set; } = "";
}
