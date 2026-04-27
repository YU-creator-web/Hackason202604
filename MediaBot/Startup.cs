using MediaBot.Bot;
using MediaBot.Controllers;
using MediaBot.Models;
using Owin;
using System.Web.Http;

/// <summary>
/// OWIN スタートアップクラス。
/// ASP.NET Core の Program.cs + Startup.cs の役割を担う。
///
/// OWIN（Open Web Interface for .NET）は .NET Framework 時代の
/// Web サーバー抽象化レイヤー。ASP.NET Core の前身。
/// </summary>
public class Startup
{
    // DI の代わりに静的プロパティでサービスを渡す
    public static BotService? BotService { get; set; }
    public static AppSettings? Settings { get; set; }

    public void Configuration(IAppBuilder app)
    {
        var config = new HttpConfiguration();

        // ルーティング設定（ASP.NET Core の MapControllers() に相当）
        config.Routes.MapHttpRoute(
            name: "DefaultApi",
            routeTemplate: "api/{controller}/{action}/{id}",
            defaults: new { id = RouteParameter.Optional }
        );

        // コントローラーにサービスを注入（シンプルな手動 DI）
        config.DependencyResolver = new SimpleDependencyResolver(BotService!, Settings!);

        app.UseWebApi(config);

        Console.WriteLine("✅ OWIN Web API 設定完了");
    }
}
