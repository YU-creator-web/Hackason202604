using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Graph.Communications.Client.Authentication;
using Microsoft.Identity.Client;
using System.Threading;

namespace MediaBot.Bot;

/// <summary>
/// Azure AD からアクセストークンを取得し、Graph Communications SDK の
/// 送信リクエストに Bearer トークンを付与する認証プロバイダー。
///
/// MSAL（Microsoft Authentication Library）を使ってクライアント資格情報フロー
///（Client Credentials Flow）でトークンを取得する。
/// これはサービス間通信（ユーザーなし）の標準的な認証方式。
///
/// IRequestAuthenticationProvider は Communications SDK が要求するインターフェース。
/// ASP.NET Core の IAuthenticationHandler に相当。
/// </summary>
public class AuthenticationProvider : IRequestAuthenticationProvider
{
    private readonly IConfidentialClientApplication _msalApp;

    public AuthenticationProvider(string appId, string appSecret, string tenantId)
    {
        // MSAL: Microsoft Authentication Library
        // ConfidentialClientApplication = サーバーサイドアプリ（秘密鍵を持てる）
        _msalApp = ConfidentialClientApplicationBuilder
            .Create(appId)
            .WithClientSecret(appSecret)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .Build();
    }

    /// <summary>
    /// SDK が Graph API にリクエストを送る前に呼ばれる。
    /// Bearer トークンをリクエストヘッダーに付与する。
    /// </summary>
    public async Task AuthenticateOutboundRequestAsync(HttpRequestMessage request, string tenantId)
    {
        // .default スコープ = App Registration に設定したすべての権限を要求
        var result = await _msalApp
            .AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" })
            .ExecuteAsync();

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", result.AccessToken);
    }

    /// <summary>
    /// Teams から届いた着信通知の署名を検証する。
    /// 本番環境では JWT トークンの検証が必要だが、開発中は省略。
    /// RequestValidationResult.IsValid = true を返してすべて受け入れる。
    /// </summary>
    public Task<RequestValidationResult> ValidateInboundRequestAsync(HttpRequestMessage request)
    {
        // TODO: 本番では Teams が送る JWT を検証する
        return Task.FromResult(new RequestValidationResult { IsValid = true });
    }
}
