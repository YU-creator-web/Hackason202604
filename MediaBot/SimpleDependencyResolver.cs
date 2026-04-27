using MediaBot.Bot;
using MediaBot.Controllers;
using MediaBot.Models;
using System;
using System.Collections.Generic;
using System.Web.Http.Dependencies;

/// <summary>
/// シンプルな DI リゾルバー。
/// ASP.NET Core の ServiceProvider に相当するが、
/// .NET Framework WebAPI では自分で実装する必要がある。
/// </summary>
public class SimpleDependencyResolver : IDependencyResolver
{
    private readonly BotService _botService;
    private readonly AppSettings _settings;

    public SimpleDependencyResolver(BotService botService, AppSettings settings)
    {
        _botService = botService;
        _settings = settings;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(PlatformCallController))
            return new PlatformCallController(_botService);
        return null;
    }

    public IEnumerable<object> GetServices(Type serviceType) => Array.Empty<object>();
    public IDependencyScope BeginScope() => this;
    public void Dispose() { }
}
