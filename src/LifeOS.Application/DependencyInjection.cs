using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<LifeAreaService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<TaskService>();
        services.AddScoped<SprintService>();
        services.AddScoped<TodayService>();
        return services;
    }
}
