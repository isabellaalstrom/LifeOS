using LifeOS.Application.Abstractions;
using LifeOS.Application.Ai;
using LifeOS.Application.Goals;
using LifeOS.Application.Habits;
using LifeOS.Application.Notifications;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Application.Reminders;
using LifeOS.Application.Retros;
using LifeOS.Domain.Ai;
using LifeOS.Domain.Notifications;
using LifeOS.Domain.Time;
using Microsoft.Extensions.Options;

namespace LifeOS.Tests.Infrastructure;

public sealed class FakeClock(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider, IUserClock
{
    public DateTimeOffset Now { get; set; } = now;

    public TimeZoneInfo Zone { get; } = zone ?? TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public DateTimeOffset UtcNow => Now;

    private long _ticks;

    /// <summary>Entity timestamps follow the test clock; each call is a tick later so ordering by creation time stays stable.</summary>
    public override DateTimeOffset GetUtcNow() => Now.AddTicks(++_ticks);

    public DateOnly Today => PeriodCalculator.LocalDate(Now, Zone);

    public void Set(string utcIso) => Now = DateTimeOffset.Parse(utcIso, null, global::System.Globalization.DateTimeStyles.AssumeUniversal);
}

public sealed class TestDbContextFactory(TestDatabase db, ICurrentUser user, TimeProvider? time = null) : IAppDbContextFactory
{
    public IAppDbContext Create() => db.CreateContext(user, time);
}

/// <summary>A scripted AI provider. Records every request so tests can inspect exactly what would have been sent.</summary>
public sealed class FakeAiClient : IAiClient
{
    private readonly Queue<AiCompletion> _responses = new();

    public bool IsConfigured { get; set; } = true;

    public List<AiRequest> Requests { get; } = [];

    public void Enqueue(string json) => _responses.Enqueue(AiCompletion.Success(json));

    public void EnqueueFailure(AiFailure failure) => _responses.Enqueue(AiCompletion.Failed(failure));

    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : AiCompletion.Failed(AiFailure.Unavailable));
    }

    /// <summary>Everything sent in the latest request as one string (system prompt and messages).</summary>
    public string LastSentText => Requests.Count == 0
        ? string.Empty
        : Requests[^1].System + "\n" + string.Join("\n", Requests[^1].Messages.Select(m => m.Content));
}

/// <summary>A notification channel that records what it was asked to send.</summary>
public sealed class FakeSender(NotificationChannel channel) : INotificationSender
{
    private readonly Queue<SendOutcome> _outcomes = new();

    public NotificationChannel Channel { get; } = channel;

    public bool IsAvailable { get; set; } = true;

    public List<NotificationPayload> Sent { get; } = [];

    /// <summary>The next call returns this instead of the default success.</summary>
    public void Enqueue(SendOutcome outcome) => _outcomes.Enqueue(outcome);

    public Task<SendOutcome> SendAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        Sent.Add(payload);
        return Task.FromResult(_outcomes.Count > 0 ? _outcomes.Dequeue() : SendOutcome.Ok());
    }
}

public sealed class FakeProfileStore : IUserProfileStore
{
    public string TimeZoneId { get; private set; } = "Europe/Stockholm";

    public Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeZoneId);

    public Task<bool> SetTimeZoneIdAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Task.FromResult(false);
        }

        TimeZoneId = zoneId;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<string>> ListOwnerIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(["user-a", "user-b"]);
}

/// <summary>An in-memory reminders list. With <see cref="Lag"/>, added items only show up after <see cref="Release"/> (like iCloud).</summary>
public sealed class FakeReminderClient : IReminderClient
{
    private readonly List<ReminderItem> _delayed = [];

    public bool IsConfigured { get; set; } = true;

    public string? ListName => IsConfigured ? "todo.test" : null;

    public bool Lag { get; set; }

    public bool Fail { get; set; }

    public List<ReminderItem> Items { get; } = [];

    public int AddCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    /// <summary>Something the owner added on the phone.</summary>
    public ReminderItem Phone(string summary, DateOnly? due = null, DateTimeOffset? dueAt = null, bool done = false)
    {
        var item = new ReminderItem("Reminder/" + Guid.NewGuid().ToString("N"), summary, done, due, dueAt);
        Items.Add(item);
        return item;
    }

    public void Release()
    {
        Items.AddRange(_delayed);
        _delayed.Clear();
    }

    public void Replace(string uid, Func<ReminderItem, ReminderItem> change)
    {
        var i = Items.FindIndex(x => x.Uid == uid);
        Items[i] = change(Items[i]);
    }

    public Task<IReadOnlyList<ReminderItem>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        Fail ? throw new ReminderException("down") : Task.FromResult<IReadOnlyList<ReminderItem>>(Items.ToList());

    public Task AddAsync(string summary, DateOnly? due, CancellationToken cancellationToken = default)
    {
        AddCalls++;
        var item = new ReminderItem("Reminder/" + Guid.NewGuid().ToString("N"), summary, false, due, null);
        (Lag ? _delayed : Items).Add(item);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(string uid, string? summary, DateOnly? due, bool? done, CancellationToken cancellationToken = default)
    {
        UpdateCalls++;
        Replace(uid, i => i with
        {
            Summary = summary ?? i.Summary,
            Due = due ?? (due is null && i.Due is not null ? i.Due : null),
            DueAt = due is not null ? null : i.DueAt,
            Done = done ?? i.Done,
        });
        return Task.CompletedTask;
    }
}

/// <summary>Real services on a real migrated SQLite database with a controllable user and clock.</summary>
public sealed class ServiceHarness : IDisposable
{
    // Wednesday 14 Oct 2026 10:00 UTC. The week starts Monday 12 Oct (local midnight = 11 Oct 22:00 UTC).
    public const string Wednesday = "2026-10-14T10:00:00Z";

    public static readonly DateOnly Week = new(2026, 10, 12);
    public static readonly DateOnly NextWeek = new(2026, 10, 19);

    private readonly bool _ownsDatabase;

    /// <param name="shareWith">Another harness whose database and clock to use, to act as a second owner.</param>
    public ServiceHarness(string userId = "user-a", string now = Wednesday, ServiceHarness? shareWith = null)
    {
        _ownsDatabase = shareWith is null;
        Db = shareWith?.Db ?? new TestDatabase();
        User = new FakeCurrentUser(userId);
        Clock = shareWith?.Clock ?? new FakeClock(DateTimeOffset.Parse(now, null, global::System.Globalization.DateTimeStyles.AssumeUniversal));
        Factory = new TestDbContextFactory(Db, User, Clock);

        Areas = new LifeAreaService(Factory);
        GoalRecorder = new GoalProgressRecorder(Clock);
        HabitRecorder = new HabitCompletionRecorder(Clock, GoalRecorder);
        Tasks = new TaskService(Factory, Clock, GoalRecorder, HabitRecorder);
        Sprints = new SprintService(Factory, Clock);
        Today = new TodayService(Factory, Clock, Sprints);
        Goals = new GoalService(Factory, Clock, GoalRecorder);
        Habits = new HabitService(Factory, Clock, HabitRecorder, Tasks, Sprints);
        RetroStats = new RetroStatsService(Factory, Clock, Sprints);
        Retros = new RetroService(Factory, Clock, RetroStats, Tasks);

        AiOptions = new AiOptions();
        var aiOptions = Options.Create(AiOptions);
        AiClient = new FakeAiClient();
        Profile = new FakeProfileStore();
        Prefs = new PreferenceService(Factory, Profile);
        Contexts = new AiContextBuilder(Retros, RetroStats, Goals, Habits, Sprints, Tasks, Clock, aiOptions);
        Proposals = new AiProposalService(Factory, Clock, Sprints, Retros, Goals);
        Coach = new AiCoachService(AiClient, Factory, Clock, Prefs, Contexts, Proposals, aiOptions);

        PushSender = new FakeSender(NotificationChannel.WebPush);
        NtfySender = new FakeSender(NotificationChannel.Ntfy);
        Planner = new NotificationPlanner(Factory, Clock, Retros);
        Dispatcher = new NotificationDispatcher(Factory, Clock, [PushSender, NtfySender]);
        Notifications = new NotificationService(Factory, Clock, new NotificationRunner(Planner, Dispatcher));
        Subscriptions = new PushSubscriptionService(Factory);
        ReminderClient = new FakeReminderClient();
        Reminders = new ReminderSyncService(Factory, Clock, ReminderClient, Tasks);
    }

    public TestDatabase Db { get; }

    public FakeCurrentUser User { get; }

    public FakeClock Clock { get; }

    public TestDbContextFactory Factory { get; }

    public LifeAreaService Areas { get; }

    public TaskService Tasks { get; }

    public SprintService Sprints { get; }

    public TodayService Today { get; }

    public GoalProgressRecorder GoalRecorder { get; }

    public HabitCompletionRecorder HabitRecorder { get; }

    public GoalService Goals { get; }

    public HabitService Habits { get; }

    public RetroStatsService RetroStats { get; }

    public RetroService Retros { get; }

    public AiOptions AiOptions { get; }

    public FakeAiClient AiClient { get; }

    public FakeProfileStore Profile { get; }

    public PreferenceService Prefs { get; }

    public AiContextBuilder Contexts { get; }

    public AiProposalService Proposals { get; }

    public AiCoachService Coach { get; }

    public FakeSender PushSender { get; }

    public FakeSender NtfySender { get; }

    public NotificationPlanner Planner { get; }

    public NotificationDispatcher Dispatcher { get; }

    public NotificationService Notifications { get; }

    public PushSubscriptionService Subscriptions { get; }

    public FakeReminderClient ReminderClient { get; }

    public ReminderSyncService Reminders { get; }

    /// <summary>A second set of services for another owner against the same database and clock.</summary>
    public ServiceHarness ForUser(string userId) => new(userId, shareWith: this);

    public async Task<Guid> NewPlannableTaskAsync(string title, int? estimate = null) =>
        await Tasks.CreateAsync(new TaskInput(title, EstimateMinutes: estimate));

    /// <summary>Turns AI on for this owner (it is off by default).</summary>
    public Task EnableAiAsync(AiCoachMode mode = AiCoachMode.Balanced) => Prefs.SetAiAsync(true, mode);

    public void Dispose()
    {
        if (_ownsDatabase)
        {
            Db.Dispose();
        }
    }
}
