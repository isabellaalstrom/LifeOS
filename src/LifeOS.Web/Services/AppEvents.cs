namespace LifeOS.Web.Services;

/// <summary>Per-circuit notifications so components can refresh after changes made elsewhere (e.g. quick capture).</summary>
public sealed class AppEvents
{
    public event Func<Task>? TasksChanged;

    public async Task NotifyTasksChangedAsync()
    {
        if (TasksChanged is null)
        {
            return;
        }

        foreach (var handler in TasksChanged.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }
}
