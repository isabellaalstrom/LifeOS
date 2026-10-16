using LifeOS.Application.Planning;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace LifeOS.Web.Components.Shared;

/// <summary>Shared plumbing for planning pages: localized strings, friendly error handling for rule violations.</summary>
public abstract class PlanningComponentBase : ComponentBase
{
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;

    [Inject] protected IDialogService DialogService { get; set; } = default!;

    [Inject] protected IStringLocalizer<SharedResource> L { get; set; } = default!;

    /// <summary>Runs an action and shows a calm message if a planning rule was violated. Returns true on success.</summary>
    protected async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (PlanningException ex)
        {
            Snackbar.Add(ErrorText(ex.Code), Severity.Warning);
            return false;
        }
    }

    protected string ErrorText(string code)
    {
        var text = L["Err_" + code];
        return text.ResourceNotFound ? L["Err_Generic"] : text;
    }

    protected static readonly DialogOptions DialogOptions = new()
    {
        FullWidth = true,
        MaxWidth = MaxWidth.Small,
        CloseOnEscapeKey = true,
    };

    /// <summary>Shows a dialog and returns its result, or null if it was cancelled.</summary>
    protected async Task<DialogResult?> ShowDialogAsync<TDialog>(string title, DialogParameters? parameters = null)
        where TDialog : ComponentBase
    {
        var reference = await DialogService.ShowAsync<TDialog>(title, parameters ?? [], DialogOptions);
        var result = await reference.Result;
        return result is { Canceled: false } ? result : null;
    }
}
