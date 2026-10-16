namespace LifeOS.Domain.Planning;

public enum CapacityStatus
{
    /// <summary>No capacity set; nothing to compare against.</summary>
    NotSet,
    Ok,
    NearLimit,
    Over,
}

/// <summary>Capacity is advisory: it produces a status for the UI and never blocks planning.</summary>
public static class SprintCapacity
{
    public const double NearLimitRatio = 0.9;

    public static CapacityStatus Evaluate(int plannedMinutes, int? capacityMinutes)
    {
        if (capacityMinutes is null or <= 0)
        {
            return CapacityStatus.NotSet;
        }

        if (plannedMinutes > capacityMinutes)
        {
            return CapacityStatus.Over;
        }

        return plannedMinutes >= capacityMinutes * NearLimitRatio ? CapacityStatus.NearLimit : CapacityStatus.Ok;
    }
}

/// <summary>
/// Scope numbers for retrospectives. Original scope is what was in the sprint at the boundary
/// (explicit start, or the start of the week); everything after counts as a change.
/// </summary>
/// <param name="Original">Tasks in the sprint at the boundary.</param>
/// <param name="AddedAfterStart">Distinct tasks added after the boundary that were not in the original scope.</param>
/// <param name="RemovedAfterStart">Distinct tasks removed after the boundary (excluding carry-over) and no longer in the sprint.</param>
/// <param name="CarriedOut">Distinct tasks moved on to another sprint when this sprint was closed.</param>
/// <param name="CarriedIn">Distinct tasks that arrived by carry-over from an earlier sprint.</param>
/// <param name="Current">Tasks in the sprint now.</param>
public sealed record SprintScopeStats(int Original, int AddedAfterStart, int RemovedAfterStart, int CarriedOut, int CarriedIn, int Current)
{
    public static readonly SprintScopeStats Empty = new(0, 0, 0, 0, 0, 0);
}

public static class SprintScope
{
    public static SprintScopeStats Compute(IEnumerable<SprintScopeChange> changes, DateTimeOffset boundaryUtc)
    {
        var byTask = changes
            .OrderBy(c => c.OccurredUtc)
            .GroupBy(c => c.TaskItemId);

        int original = 0, added = 0, removed = 0, carriedOut = 0, carriedIn = 0, current = 0;

        foreach (var events in byTask)
        {
            var list = events.ToList();

            var presentAtBoundary = false;
            foreach (var e in list.Where(e => e.OccurredUtc < boundaryUtc))
            {
                presentAtBoundary = e.Kind == ScopeChangeKind.Added;
            }

            var after = list.Where(e => e.OccurredUtc >= boundaryUtc).ToList();
            var presentNow = after.Count > 0 ? after[^1].Kind == ScopeChangeKind.Added : presentAtBoundary;

            if (presentAtBoundary)
            {
                original++;
            }
            else if (after.Any(e => e.Kind == ScopeChangeKind.Added))
            {
                added++;
            }

            if (!presentNow)
            {
                if (after.Any(e => e.Kind == ScopeChangeKind.Removed && e.Reason == ScopeChangeReason.CarryOver))
                {
                    carriedOut++;
                }
                else if (after.Any(e => e.Kind == ScopeChangeKind.Removed))
                {
                    removed++;
                }
            }

            if (list.Any(e => e.Kind == ScopeChangeKind.Added && e.Reason == ScopeChangeReason.CarryOver))
            {
                carriedIn++;
            }

            if (presentNow)
            {
                current++;
            }
        }

        return new SprintScopeStats(original, added, removed, carriedOut, carriedIn, current);
    }
}
