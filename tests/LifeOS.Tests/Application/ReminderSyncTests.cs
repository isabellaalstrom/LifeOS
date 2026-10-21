using LifeOS.Application.Organize;
using LifeOS.Application.Reminders;
using LifeOS.Domain.Tasks;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class ReminderSyncTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14); // "today" in the harness

    private static async Task<ServiceHarness> WithSyncAsync(bool import = true, bool push = true)
    {
        var h = new ServiceHarness();
        await h.Reminders.SetEnabledAsync(import, push);
        return h;
    }

    private static async Task<IReadOnlyList<TaskView>> BacklogAsync(ServiceHarness h) => await h.Tasks.ListBacklogAsync();

    // ---------- off by default ----------

    [Fact]
    public async Task Nothing_is_read_or_written_until_the_owner_switches_it_on()
    {
        using var h = new ServiceHarness();
        h.ReminderClient.Phone("Köp mjölk");

        Assert.Equal(ReminderSyncStatus.Disabled, (await h.Reminders.SyncAsync()).Status);
        Assert.Empty(await BacklogAsync(h));

        var status = await h.Reminders.GetStatusAsync();
        Assert.Equal((true, "todo.test", false, false), (status.Configured, status.ListName, status.ImportEnabled, status.PushEnabled));
    }

    [Fact]
    public async Task Without_a_configured_list_the_connection_reports_not_configured()
    {
        using var h = await WithSyncAsync();
        h.ReminderClient.IsConfigured = false;

        Assert.Equal(ReminderSyncStatus.NotConfigured, (await h.Reminders.SyncAsync()).Status);
        Assert.False((await h.Reminders.GetStatusAsync()).Configured);
    }

    // ---------- import ----------

    [Fact]
    public async Task Open_reminders_become_backlog_tasks_once_with_their_due_date_in_the_owners_time_zone()
    {
        using var h = await WithSyncAsync(push: false);
        h.ReminderClient.Phone("Köp mjölk");
        h.ReminderClient.Phone("Pakethållare", dueAt: DateTimeOffset.Parse("2026-10-21T22:30:00Z"));   // 00:30 on the 22nd in Stockholm
        h.ReminderClient.Phone("Klarmarkera", due: new DateOnly(2026, 11, 1));
        h.ReminderClient.Phone("Redan gjort", done: true);

        var first = await h.Reminders.SyncAsync();
        var second = await h.Reminders.SyncAsync();
        await h.Reminders.SyncAsync();

        Assert.Equal((ReminderSyncStatus.Ok, 3, 0), (first.Status, first.Imported, first.Pushed));
        Assert.Equal(0, second.Imported);
        var tasks = await BacklogAsync(h);
        Assert.Equal(["Klarmarkera", "Köp mjölk", "Pakethållare"], tasks.Select(t => t.Title).Order(StringComparer.Ordinal));
        Assert.Equal(new DateOnly(2026, 10, 22), tasks.Single(t => t.Title == "Pakethållare").DueDate);
        Assert.Equal(new DateOnly(2026, 11, 1), tasks.Single(t => t.Title == "Klarmarkera").DueDate);
        Assert.Null(tasks.Single(t => t.Title == "Köp mjölk").DueDate);
        Assert.Equal(0, h.ReminderClient.AddCalls);
        Assert.NotNull((await h.Reminders.GetStatusAsync()).LastSyncUtc);
    }

    [Fact]
    public async Task Import_can_be_off_while_sending_is_on()
    {
        using var h = await WithSyncAsync(import: false, push: true);
        h.ReminderClient.Phone("Köp mjölk");

        await h.Reminders.SyncAsync();

        Assert.Empty(await BacklogAsync(h));
    }

    // ---------- done / not done ----------

    [Fact]
    public async Task Ticking_a_reminder_finishes_the_task_and_reopening_it_reopens_the_task()
    {
        using var h = await WithSyncAsync(push: false);
        var reminder = h.ReminderClient.Phone("Köp mjölk");
        await h.Reminders.SyncAsync();
        var task = (await BacklogAsync(h)).Single();

        h.ReminderClient.Replace(reminder.Uid, r => r with { Done = true });
        var result = await h.Reminders.SyncAsync();
        Assert.Equal(1, result.Updated);
        Assert.Equal(TaskItemStatus.Done, (await h.Tasks.GetAsync(task.Id))!.Task.Status);

        h.ReminderClient.Replace(reminder.Uid, r => r with { Done = false });
        await h.Reminders.SyncAsync();
        Assert.Equal(TaskItemStatus.ToDo, (await h.Tasks.GetAsync(task.Id))!.Task.Status);
    }

    [Fact]
    public async Task Finishing_the_task_ticks_the_reminder_and_nothing_bounces_back()
    {
        using var h = await WithSyncAsync(push: false);
        var reminder = h.ReminderClient.Phone("Köp mjölk");
        await h.Reminders.SyncAsync();
        var task = (await BacklogAsync(h)).Single();

        await h.Tasks.SetStatusAsync(task.Id, TaskItemStatus.Done);
        await h.Reminders.SyncAsync();
        await h.Reminders.SyncAsync();

        Assert.True(h.ReminderClient.Items.Single(i => i.Uid == reminder.Uid).Done);
        Assert.Equal(1, h.ReminderClient.UpdateCalls);                                         // only the one tick
        Assert.Equal(TaskItemStatus.Done, (await h.Tasks.GetAsync(task.Id))!.Task.Status);
    }

    // ---------- title and due date ----------

    [Fact]
    public async Task The_side_that_changed_wins_for_title_and_due_date()
    {
        using var h = await WithSyncAsync(push: false);
        var a = h.ReminderClient.Phone("Gammalt namn", due: new DateOnly(2026, 11, 1));
        var b = h.ReminderClient.Phone("Annan sak");
        await h.Reminders.SyncAsync();
        var taskA = (await BacklogAsync(h)).Single(t => t.Title == "Gammalt namn");
        var taskB = (await BacklogAsync(h)).Single(t => t.Title == "Annan sak");

        // The phone renames and moves A; LifeOS renames B and gives it a date.
        h.ReminderClient.Replace(a.Uid, r => r with { Summary = "Nytt namn", Due = new DateOnly(2026, 11, 5) });
        await h.Tasks.UpdateAsync(taskB.Id, new TaskInput("Bytt i LifeOS", DueDate: new DateOnly(2026, 12, 24)));
        await h.Reminders.SyncAsync();

        var tasks = await BacklogAsync(h);
        Assert.Equal(("Nytt namn", new DateOnly(2026, 11, 5)), (tasks.Single(t => t.Id == taskA.Id).Title, tasks.Single(t => t.Id == taskA.Id).DueDate));
        var remoteB = h.ReminderClient.Items.Single(i => i.Uid == b.Uid);
        Assert.Equal(("Bytt i LifeOS", (DateOnly?)new DateOnly(2026, 12, 24)), (remoteB.Summary, remoteB.Due));

        // Once synced, another pass changes nothing.
        var calls = h.ReminderClient.UpdateCalls;
        var again = await h.Reminders.SyncAsync();
        Assert.Equal((0, calls), (again.Updated, h.ReminderClient.UpdateCalls));
    }

    [Fact]
    public async Task Clearing_the_date_on_the_phone_clears_it_on_the_task()
    {
        using var h = await WithSyncAsync(push: false);
        var reminder = h.ReminderClient.Phone("Med datum", due: new DateOnly(2026, 11, 1));
        await h.Reminders.SyncAsync();

        h.ReminderClient.Replace(reminder.Uid, r => r with { Due = null });
        await h.Reminders.SyncAsync();

        Assert.Null((await BacklogAsync(h)).Single().DueDate);
    }

    [Fact]
    public async Task A_reminder_deleted_on_the_phone_leaves_the_task_alone_and_is_not_sent_again()
    {
        using var h = await WithSyncAsync();
        var reminder = h.ReminderClient.Phone("Köp mjölk", due: Wednesday);
        await h.Reminders.SyncAsync();
        var task = (await BacklogAsync(h)).Single();

        h.ReminderClient.Items.RemoveAll(i => i.Uid == reminder.Uid);
        await h.Reminders.SyncAsync();
        await h.Reminders.SyncAsync();

        Assert.Equal(task.Id, Assert.Single(await BacklogAsync(h)).Id);
        Assert.Empty(h.ReminderClient.Items);
        Assert.Equal(0, h.ReminderClient.AddCalls);
    }

    // ---------- sending tasks to the list ----------

    [Fact]
    public async Task Tasks_due_or_in_todays_focus_are_sent_once_and_others_are_not()
    {
        using var h = await WithSyncAsync(import: false);
        await h.Tasks.CreateAsync(new TaskInput("Förfaller idag", DueDate: Wednesday));
        await h.Tasks.CreateAsync(new TaskInput("Försenad", DueDate: Wednesday.AddDays(-3)));
        var focus = await h.Tasks.CreateAsync(new TaskInput("I fokus"));
        await h.Tasks.SetFocusAsync(focus, Wednesday);
        await h.Tasks.CreateAsync(new TaskInput("Senare", DueDate: Wednesday.AddDays(5)));
        await h.Tasks.CreateAsync(new TaskInput("Utan datum"));
        var done = await h.Tasks.CreateAsync(new TaskInput("Klar", DueDate: Wednesday));
        await h.Tasks.SetStatusAsync(done, TaskItemStatus.Done);
        var parent = await h.Tasks.CreateAsync(new TaskInput("Överordnad"));
        await h.Tasks.AddSubtaskAsync(parent, "Deluppgift");

        var first = await h.Reminders.SyncAsync();
        await h.Reminders.SyncAsync();
        await h.Reminders.SyncAsync();

        Assert.Equal(3, first.Pushed);
        Assert.Equal(["Förfaller idag", "Försenad", "I fokus"], h.ReminderClient.Items.Select(i => i.Summary).Order(StringComparer.Ordinal));
        Assert.Equal(Wednesday, h.ReminderClient.Items.Single(i => i.Summary == "I fokus").Due);                // no date: shows up under today
        Assert.Equal(Wednesday.AddDays(-3), h.ReminderClient.Items.Single(i => i.Summary == "Försenad").Due);
        Assert.Equal(3, h.ReminderClient.AddCalls);
    }

    [Fact]
    public async Task A_task_sent_to_the_list_is_never_imported_back_as_a_second_task_even_when_the_list_is_slow()
    {
        using var h = await WithSyncAsync();
        h.ReminderClient.Lag = true;
        var task = await h.Tasks.CreateAsync(new TaskInput("Lämna paket", DueDate: Wednesday));

        await h.Reminders.SyncAsync();                       // sent, but not visible yet
        await h.Reminders.SyncAsync();                       // still not visible: must not be sent again or imported
        Assert.Equal(1, h.ReminderClient.AddCalls);
        Assert.Empty(h.ReminderClient.Items);

        h.ReminderClient.Release();                          // now it appears
        var third = await h.Reminders.SyncAsync();
        await h.Reminders.SyncAsync();

        Assert.Equal(0, third.Imported);
        Assert.Equal(task, Assert.Single(await BacklogAsync(h)).Id);
        Assert.Equal(1, h.ReminderClient.AddCalls);

        // From now on the two stay in step.
        await h.Tasks.SetStatusAsync(task, TaskItemStatus.Done);
        await h.Reminders.SyncAsync();
        Assert.True(Assert.Single(h.ReminderClient.Items).Done);
    }

    [Fact]
    public async Task A_reminder_that_never_showed_up_is_forgotten_after_a_day()
    {
        using var h = await WithSyncAsync(import: false);
        h.ReminderClient.Lag = true;
        await h.Tasks.CreateAsync(new TaskInput("Försvinner", DueDate: Wednesday));
        await h.Reminders.SyncAsync();

        h.Clock.Set("2026-10-15T12:00:00Z");                 // more than 24 hours later; the task is still due
        h.ReminderClient.Lag = false;
        await h.Reminders.SyncAsync();                       // the stale pending link is dropped...
        await h.Reminders.SyncAsync();                       // ...and the task is sent again, now visibly

        Assert.Equal(2, h.ReminderClient.AddCalls);
        Assert.Single(h.ReminderClient.Items);
    }

    [Fact]
    public async Task At_most_twenty_tasks_are_sent_in_one_pass()
    {
        using var h = await WithSyncAsync(import: false);
        for (var i = 0; i < 25; i++)
        {
            await h.Tasks.CreateAsync(new TaskInput($"Uppgift {i}", DueDate: Wednesday));
        }

        var first = await h.Reminders.SyncAsync();
        var second = await h.Reminders.SyncAsync();

        Assert.Equal((ReminderSyncService.MaxPushPerPass, 5), (first.Pushed, second.Pushed));
        Assert.Equal(25, h.ReminderClient.Items.Count);
    }

    // ---------- failures and ownership ----------

    [Fact]
    public async Task A_failing_list_reports_a_short_error_changes_nothing_and_recovers()
    {
        using var h = await WithSyncAsync();
        h.ReminderClient.Phone("Köp mjölk");
        h.ReminderClient.Fail = true;

        var failed = await h.Reminders.SyncAsync();

        Assert.Equal(ReminderSyncStatus.Failed, failed.Status);
        Assert.Empty(await BacklogAsync(h));
        var status = await h.Reminders.GetStatusAsync();
        Assert.Equal("down", status.LastError);
        Assert.Null(status.LastSyncUtc);

        h.ReminderClient.Fail = false;
        var ok = await h.Reminders.SyncAsync();
        Assert.Equal((ReminderSyncStatus.Ok, 1), (ok.Status, ok.Imported));
        Assert.Null((await h.Reminders.GetStatusAsync()).LastError);
    }

    [Fact]
    public async Task The_switches_belong_to_each_owner()
    {
        using var h = await WithSyncAsync();
        var other = h.ForUser("user-b");

        var status = await other.Reminders.GetStatusAsync();
        Assert.Equal((false, false), (status.ImportEnabled, status.PushEnabled));
        other.ReminderClient.Phone("Andras");
        Assert.Equal(ReminderSyncStatus.Disabled, (await other.Reminders.SyncAsync()).Status);

        var mine = await h.Reminders.GetStatusAsync();
        Assert.Equal((true, true), (mine.ImportEnabled, mine.PushEnabled));
    }
}
