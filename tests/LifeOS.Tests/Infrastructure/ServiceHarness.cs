using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Time;

namespace LifeOS.Tests.Infrastructure;

public sealed class FakeClock(DateTimeOffset now, TimeZoneInfo? zone = null) : IUserClock
{
    public DateTimeOffset Now { get; set; } = now;

    public TimeZoneInfo Zone { get; } = zone ?? TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public DateTimeOffset UtcNow => Now;

    public DateOnly Today => PeriodCalculator.LocalDate(Now, Zone);

    public void Set(string utcIso) => Now = DateTimeOffset.Parse(utcIso, null, System.Globalization.DateTimeStyles.AssumeUniversal);
}

public sealed class TestDbContextFactory(TestDatabase db, ICurrentUser user) : IAppDbContextFactory
{
    public IAppDbContext Create() => db.CreateContext(user);
}

/// <summary>Real services on a real migrated SQLite database with a controllable user and clock.</summary>
public sealed class ServiceHarness : IDisposable
{
    // Wednesday 14 Oct 2026 10:00 UTC. The week starts Monday 12 Oct (local midnight = 11 Oct 22:00 UTC).
    public const string Wednesday = "2026-10-14T10:00:00Z";

    public static readonly DateOnly Week = new(2026, 10, 12);
    public static readonly DateOnly NextWeek = new(2026, 10, 19);

    public ServiceHarness(string userId = "user-a", string now = Wednesday)
    {
        Db = new TestDatabase();
        User = new FakeCurrentUser(userId);
        Clock = new FakeClock(DateTimeOffset.Parse(now, null, System.Globalization.DateTimeStyles.AssumeUniversal));
        Factory = new TestDbContextFactory(Db, User);
        Areas = new LifeAreaService(Factory);
        Projects = new ProjectService(Factory);
        Tasks = new TaskService(Factory, Clock);
        Sprints = new SprintService(Factory, Clock);
        Today = new TodayService(Factory, Clock, Sprints, Tasks);
    }

    public TestDatabase Db { get; }

    public FakeCurrentUser User { get; }

    public FakeClock Clock { get; }

    public TestDbContextFactory Factory { get; }

    public LifeAreaService Areas { get; }

    public ProjectService Projects { get; }

    public TaskService Tasks { get; }

    public SprintService Sprints { get; }

    public TodayService Today { get; }

    /// <summary>A second set of services for another owner against the same database.</summary>
    public ServiceHarnessView ForUser(string userId)
    {
        var user = new FakeCurrentUser(userId);
        var factory = new TestDbContextFactory(Db, user);
        var tasks = new TaskService(factory, Clock);
        var sprints = new SprintService(factory, Clock);
        return new ServiceHarnessView(tasks, sprints, new LifeAreaService(factory), new ProjectService(factory));
    }

    public async Task<Guid> NewPlannableTaskAsync(string title, int? estimate = null) =>
        await Tasks.CreateAsync(new TaskInput(title, EstimateMinutes: estimate));

    public void Dispose() => Db.Dispose();
}

public sealed record ServiceHarnessView(TaskService Tasks, SprintService Sprints, LifeAreaService Areas, ProjectService Projects);
