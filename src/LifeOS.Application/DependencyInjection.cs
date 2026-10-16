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
        services.AddScoped<LifeOS.Application.Ai.PreferenceService>();
        services.AddScoped<LifeOS.Application.Ai.AiContextBuilder>();
        services.AddScoped<LifeOS.Application.Ai.AiProposalService>();
        services.AddScoped<LifeOS.Application.Ai.IAiCoachService, LifeOS.Application.Ai.AiCoachService>();
        services.AddScoped<LifeOS.Application.Notifications.PushSubscriptionService>();
        services.AddScoped<LifeOS.Application.Notifications.NotificationPlanner>();
        services.AddScoped<LifeOS.Application.Notifications.NotificationDispatcher>();
        services.AddScoped<LifeOS.Application.Notifications.NotificationRunner>();
        services.AddScoped<LifeOS.Application.Notifications.NotificationService>();
        services.AddScoped<LifeOS.Application.Notifications.OwnerOverride>();
        return services;
    }
}
