namespace LifeOS.Web.Services;

/// <summary>Per-circuit notifications so components can refresh after changes made elsewhere (e.g. quick capture).</summary>
public sealed class AppEvents
{
    public event Func<Task>? InboxChanged;

    /// <summary>Raised when a setting that changes the navigation was saved.</summary>
    public event Func<Task>? PreferencesChanged;

    public async Task NotifyPreferencesChangedAsync()
    {
        if (PreferencesChanged is null)
        {
            return;
        }

        foreach (var handler in PreferencesChanged.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }

    public async Task NotifyInboxChangedAsync()
    {
        if (InboxChanged is null)
        {
            return;
        }

        foreach (var handler in InboxChanged.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }
}
