using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Hrdr.Core.Data;
using Hrdr.Core.Services;

namespace Hrdr.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHrdrCore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HrdrDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ProjectService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<CommentService>();
        return services;
    }
}
