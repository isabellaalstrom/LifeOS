using LifeOS.Domain.Planning;

namespace LifeOS.Tests.Domain;

public class SprintRulesTests
{
    private static readonly DateTimeOffset Boundary = new(2026, 10, 11, 22, 0, 0, TimeSpan.Zero);

    private static SprintScopeChange Change(Guid task, ScopeChangeKind kind, double hoursFromBoundary, ScopeChangeReason reason = ScopeChangeReason.Planned) =>
        new() { TaskItemId = task, Kind = kind, Reason = reason, OccurredUtc = Boundary.AddHours(hoursFromBoundary), TaskTitle = "t" };

    [Theory]
    [InlineData(0, null, CapacityStatus.NotSet)]
    [InlineData(100, 0, CapacityStatus.NotSet)]
    [InlineData(0, 600, CapacityStatus.Ok)]
    [InlineData(539, 600, CapacityStatus.Ok)]
    [InlineData(540, 600, CapacityStatus.NearLimit)]
    [InlineData(600, 600, CapacityStatus.NearLimit)]
    [InlineData(601, 600, CapacityStatus.Over)]
    public void Capacity_status_is_advisory(int planned, int? capacity, CapacityStatus expected) =>
        Assert.Equal(expected, SprintCapacity.Evaluate(planned, capacity));

    [Fact]
    public void Empty_log_gives_empty_stats() =>
        Assert.Equal(SprintScopeStats.Empty, SprintScope.Compute([], Boundary));

    [Fact]
    public void Work_added_before_the_boundary_is_original_and_after_is_added()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var stats = SprintScope.Compute(
            [Change(a, ScopeChangeKind.Added, -5), Change(b, ScopeChangeKind.Added, +30)], Boundary);

        Assert.Equal(new SprintScopeStats(Original: 1, AddedAfterStart: 1, RemovedAfterStart: 0, CarriedOut: 0, CarriedIn: 0, Current: 2), stats);
    }

    [Fact]
    public void Removal_before_the_boundary_never_counts_as_original()
    {
        var a = Guid.NewGuid();
        var stats = SprintScope.Compute(
            [Change(a, ScopeChangeKind.Added, -10), Change(a, ScopeChangeKind.Removed, -5, ScopeChangeReason.Dropped)], Boundary);

        Assert.Equal(SprintScopeStats.Empty, stats);
    }

    [Fact]
    public void Removal_after_the_boundary_counts_once_and_keeps_the_task_original()
    {
        var a = Guid.NewGuid();
        var stats = SprintScope.Compute(
            [Change(a, ScopeChangeKind.Added, -10), Change(a, ScopeChangeKind.Removed, +20, ScopeChangeReason.Dropped)], Boundary);

        Assert.Equal(1, stats.Original);
        Assert.Equal(1, stats.RemovedAfterStart);
        Assert.Equal(0, stats.Current);
    }

    [Fact]
    public void Removing_and_re_adding_an_original_task_is_not_counted_as_added_or_removed()
    {
        var a = Guid.NewGuid();
        var stats = SprintScope.Compute(
            [
                Change(a, ScopeChangeKind.Added, -10),
                Change(a, ScopeChangeKind.Removed, +10, ScopeChangeReason.Dropped),
                Change(a, ScopeChangeKind.Added, +20),
            ], Boundary);

        Assert.Equal(new SprintScopeStats(1, 0, 0, 0, 0, 1), stats);
    }

    [Fact]
    public void Carry_over_is_reported_separately_from_plain_removal()
    {
        var carried = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        var arrived = Guid.NewGuid();
        var stats = SprintScope.Compute(
            [
                Change(carried, ScopeChangeKind.Added, -10),
                Change(carried, ScopeChangeKind.Removed, +100, ScopeChangeReason.CarryOver),
                Change(dropped, ScopeChangeKind.Added, -10),
                Change(dropped, ScopeChangeKind.Removed, +50, ScopeChangeReason.ReturnedToBacklog),
                Change(arrived, ScopeChangeKind.Added, -2, ScopeChangeReason.CarryOver),
            ], Boundary);

        Assert.Equal(3, stats.Original);
        Assert.Equal(1, stats.CarriedOut);
        Assert.Equal(1, stats.RemovedAfterStart);
        Assert.Equal(1, stats.CarriedIn);
        Assert.Equal(1, stats.Current);
    }

    [Fact]
    public void Carry_over_arriving_after_the_boundary_counts_as_added_and_as_carried_in()
    {
        var a = Guid.NewGuid();
        var stats = SprintScope.Compute([Change(a, ScopeChangeKind.Added, +12, ScopeChangeReason.CarryOver)], Boundary);

        Assert.Equal(0, stats.Original);
        Assert.Equal(1, stats.AddedAfterStart);
        Assert.Equal(1, stats.CarriedIn);
    }

    [Fact]
    public void Events_are_replayed_in_time_order_regardless_of_input_order()
    {
        var a = Guid.NewGuid();
        var stats = SprintScope.Compute(
            [Change(a, ScopeChangeKind.Removed, +10, ScopeChangeReason.Dropped), Change(a, ScopeChangeKind.Added, -10)], Boundary);

        Assert.Equal(1, stats.Original);
        Assert.Equal(1, stats.RemovedAfterStart);
    }
}
