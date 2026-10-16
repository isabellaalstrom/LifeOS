using System.Globalization;
using LifeOS.Domain.Tasks;
using Microsoft.Extensions.Localization;

namespace LifeOS.Web.Components.Shared;

/// <summary>Display formatting shared by pages. Dates follow the current UI culture (Swedish by default).</summary>
public static class Fmt
{
    public static string Duration(IStringLocalizer l, int? minutes)
    {
        if (minutes is null)
        {
            return string.Empty;
        }

        if (minutes <= 0)
        {
            return l["Duration_Minutes", 0];
        }

        var h = minutes.Value / 60;
        var m = minutes.Value % 60;
        return h == 0 ? l["Duration_Minutes", m] : m == 0 ? l["Duration_Hours", h] : l["Duration_HM", h, m];
    }

    public static string Day(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.CurrentCulture);

    public static string Range(DateOnly weekStart) =>
        $"{weekStart.ToString("d MMM", CultureInfo.CurrentCulture)} – {weekStart.AddDays(6).ToString("d MMM", CultureInfo.CurrentCulture)}";

    public static string PriorityKey(TaskPriority p) => "Priority_" + p;

    public static string StatusKey(TaskItemStatus s) => "Status_" + s;

    public static DateTime? ToDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

    public static DateOnly? ToDateOnly(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);

    public static TimeSpan? ToTimeSpan(TimeOnly? t) => t?.ToTimeSpan();

    public static TimeOnly? ToTimeOnly(TimeSpan? t) => t is null ? null : TimeOnly.FromTimeSpan(t.Value);
}
