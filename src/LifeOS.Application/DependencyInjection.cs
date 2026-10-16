using LifeOS.Application.Goals;
using LifeOS.Application.Habits;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Application.Retros;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<GoalProgressRecorder>();
        services.AddScoped<HabitCompletionRecorder>();
        services.AddScoped<LifeAreaService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<TaskService>();
        services.AddScoped<SprintService>();
        services.AddScoped<TodayService>();
        services.AddScoped<GoalService>();
        services.AddScoped<HabitService>();
        services.AddScoped<RetroStatsService>();
        services.AddScoped<RetroService>();
        return services;
    }
}
