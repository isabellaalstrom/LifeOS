using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
