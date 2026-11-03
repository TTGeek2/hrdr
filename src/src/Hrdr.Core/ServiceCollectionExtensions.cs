using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Hrdr.Core.Data;
using Hrdr.Core.Services;

namespace Hrdr.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHrdrCore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HrdrDbContext>(options =>
            options.UseSqlite(connectionString));

        services.TryAddSingleton<IBoardChangeNotifier, NullBoardChangeNotifier>();
        services.AddScoped<ProjectService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<CommentService>();
        services.AddScoped<BoardColumnService>();
        services.AddScoped<AgentLaunchSettingsService>();
        services.AddScoped<HerdrAgentLauncher>();
        services.AddSingleton<IHerdrCommandRunner, ProcessHerdrCommandRunner>();
        return services;
    }
}
