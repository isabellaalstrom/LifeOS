using LifeOS.Application.Abstractions;
using LifeOS.Application.Ai;
using LifeOS.Application.Notifications;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Notifications;
using LifeOS.Domain.Retros;
using LifeOS.Tests.Infrastructure;

namespace LifeOS.Tests.Application;

public class NotificationTests
{
    private static readonly NotificationPreferenceView TasksOnly =
        new(true, false, false, false, new TimeOnly(8, 0), new TimeOnly(22, 0), new TimeOnly(7, 0), false);

    private static async Task<ServiceHarness> WithDueTaskAsync(NotificationPreferenceView? prefs = null, bool subscribed = true)
    {
        var h = new ServiceHarness(); // Wednesday 14 Oct, 12:00 local
        await h.Prefs.SetNotificationsAsync(prefs ?? TasksOnly);
        await h.Tasks.CreateAsync(new TaskInput("HEMLIG-TITEL", DueDate: new DateOnly(2026, 10, 14)));
        if (subscribed)
        {
            await h.Subscriptions.SaveAsync("https://push.example/abc", "key", "auth", "TestBrowser");
        }

        return h;
    }

    /// <summary>A harness at the given time whose owner started using the app in early October (so October counts as a used period).</summary>
    private static async Task<ServiceHarness> WithHistoryAsync(string now)
    {
        var h = new ServiceHarness(now: "2026-10-05T10:00:00Z");
        await h.Tasks.CreateAsync(new TaskInput("Första uppgiften"));
        h.Clock.Set(now);
        return h;
    }

    private static async Task<NotificationView> OnlyAsync(ServiceHarness h) => Assert.Single(await h.Notifications.RecentAsync());

    // ---------- pure rules ----------

    [Theory]
    [InlineData("23:00", true)]
    [InlineData("03:30", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("12:00", false)]
    [InlineData("21:59", false)]
    [InlineData("22:00", true)]
    public void Quiet_hours_wrap_midnight(string time, bool quiet) =>
        Assert.Equal(quiet, QuietHours.IsQuiet(TimeOnly.Parse(time), new TimeOnly(22, 0), new TimeOnly(7, 0)));

    [Fact]
    public void Equal_start_and_end_means_no_quiet_hours_and_daytime_windows_work()
    {
        Assert.False(QuietHours.IsQuiet(new TimeOnly(3, 0), new TimeOnly(8, 0), new TimeOnly(8, 0)));
        Assert.True(QuietHours.IsQuiet(new TimeOnly(13, 0), new TimeOnly(12, 0), new TimeOnly(14, 0)));
        Assert.False(QuietHours.IsQuiet(new TimeOnly(14, 0), new TimeOnly(12, 0), new TimeOnly(14, 0)));
    }

    [Fact]
    public void The_next_allowed_time_is_when_quiet_hours_end()
    {
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(7, 0);
        Assert.Equal(new DateTime(2026, 10, 15, 7, 0, 0), QuietHours.NextAllowed(new DateTime(2026, 10, 14, 23, 30, 0), start, end));
        Assert.Equal(new DateTime(2026, 10, 14, 7, 0, 0), QuietHours.NextAllowed(new DateTime(2026, 10, 14, 3, 0, 0), start, end));
        Assert.Equal(new DateTime(2026, 10, 14, 12, 0, 0), QuietHours.NextAllowed(new DateTime(2026, 10, 14, 12, 0, 0), start, end));
    }

    [Fact]
    public void Retries_back_off_and_stop_after_four_attempts()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), NotificationRetryPolicy.DelayAfterFailure(1));
        Assert.Equal(TimeSpan.FromMinutes(30), NotificationRetryPolicy.DelayAfterFailure(2));
        Assert.Equal(TimeSpan.FromHours(2), NotificationRetryPolicy.DelayAfterFailure(3));
        Assert.Null(NotificationRetryPolicy.DelayAfterFailure(NotificationRetryPolicy.MaxAttempts));
    }

    // ---------- planning ----------

    [Fact]
    public async Task Nothing_is_planned_or_sent_until_a_category_is_switched_on()
    {
        using var h = new ServiceHarness();
        await h.Tasks.CreateAsync(new TaskInput("Förfallen", DueDate: new DateOnly(2026, 10, 1)));
        await h.Subscriptions.SaveAsync("https://push.example/abc", "key", "auth", null);

        Assert.Equal(0, await h.Planner.PlanAsync());
        Assert.Equal(0, await h.Dispatcher.DispatchDueAsync());

        Assert.Empty(await h.Notifications.RecentAsync());
        Assert.Empty(h.PushSender.Sent);
        var defaults = await h.Prefs.GetNotificationsAsync();
        Assert.False(defaults.TasksEnabled || defaults.GoalsEnabled || defaults.RetrosEnabled || defaults.NtfyEnabled);
    }

    [Fact]
    public async Task A_due_task_makes_one_generic_reminder_and_planning_again_never_duplicates_it()
    {
        using var h = await WithDueTaskAsync();

        Assert.Equal(1, await h.Planner.PlanAsync());
        Assert.Equal(0, await h.Planner.PlanAsync());
        Assert.Equal(0, await h.Planner.PlanAsync());

        var n = await OnlyAsync(h);
        Assert.Equal((NotificationCategory.Tasks, NotificationStatus.Pending), (n.Category, n.Status));
        Assert.DoesNotContain("HEMLIG-TITEL", n.Title + n.Body);       // counts and invitations only
        Assert.Contains("uppgift", n.Body);
        // 08:00 local on the due day.
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T06:00:00Z"), n.ScheduledForUtc);
    }

    [Fact]
    public async Task A_reminder_that_is_many_hours_late_is_skipped_instead_of_arriving_out_of_the_blue()
    {
        using var h = await WithDueTaskAsync();
        h.Clock.Set("2026-10-14T16:00:00Z"); // 18:00 local, ten hours after the 08:00 send time

        Assert.Equal(0, await h.Planner.PlanAsync());
        Assert.Empty(await h.Notifications.RecentAsync());
    }

    [Fact]
    public async Task The_send_time_inside_quiet_hours_moves_to_when_they_end()
    {
        using var h = await WithDueTaskAsync(TasksOnly with { SendTime = new TimeOnly(6, 0), QuietEnd = new TimeOnly(7, 30) });

        await h.Planner.PlanAsync();

        Assert.Equal(DateTimeOffset.Parse("2026-10-14T05:30:00Z"), (await OnlyAsync(h)).ScheduledForUtc); // 07:30 local
    }

    [Fact]
    public async Task Retro_reminders_appear_only_in_the_week_after_the_period_and_only_when_enabled()
    {
        using var h = await WithHistoryAsync("2026-11-02T10:00:00Z"); // 2 Nov: October has just ended, no retro yet
        await h.Prefs.SetNotificationsAsync(TasksOnly with { TasksEnabled = false, RetrosEnabled = true });

        Assert.Equal(1, await h.Planner.PlanAsync());
        var n = await OnlyAsync(h);
        Assert.Equal(NotificationCategory.Retros, n.Category);
        Assert.Contains("förra månaden", n.Body);

        var later = await WithHistoryAsync("2026-11-20T10:00:00Z"); // far outside the window
        await later.Prefs.SetNotificationsAsync(TasksOnly with { TasksEnabled = false, RetrosEnabled = true });
        Assert.Equal(0, await later.Planner.PlanAsync());
        later.Dispose();

        using var done = await WithHistoryAsync("2026-11-02T10:00:00Z");
        await done.Prefs.SetNotificationsAsync(TasksOnly with { TasksEnabled = false, RetrosEnabled = true });
        await done.Retros.StartOrResumeAsync(RetroType.Monthly, new DateOnly(2026, 10, 1));
        Assert.Equal(0, await done.Planner.PlanAsync()); // already reflecting on October
    }

    // ---------- dispatching ----------

    [Fact]
    public async Task A_due_notification_is_sent_once_and_recorded()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();

        Assert.Equal(1, await h.Dispatcher.DispatchDueAsync());
        Assert.Equal(0, await h.Dispatcher.DispatchDueAsync()); // not sent twice

        var payload = Assert.Single(h.PushSender.Sent);
        Assert.DoesNotContain("HEMLIG-TITEL", payload.Title + payload.Body);
        Assert.Empty(h.NtfySender.Sent);                         // ntfy is opt-in
        var n = await OnlyAsync(h);
        Assert.Equal((NotificationStatus.Sent, 1), (n.Status, n.AttemptCount));
        Assert.NotNull(n.SentUtc);
        var delivery = Assert.Single(n.Deliveries);
        Assert.Equal((NotificationChannel.WebPush, DeliveryStatus.Sent), (delivery.Channel, delivery.Status));
    }

    [Fact]
    public async Task Ntfy_is_used_in_addition_only_when_enabled()
    {
        using var h = await WithDueTaskAsync(TasksOnly with { NtfyEnabled = true });
        await h.Planner.PlanAsync();

        await h.Dispatcher.DispatchDueAsync();

        Assert.Single(h.PushSender.Sent);
        Assert.Single(h.NtfySender.Sent);
        Assert.Equal(2, (await OnlyAsync(h)).Deliveries.Count);
    }

    [Fact]
    public async Task The_home_assistant_app_is_used_only_when_enabled_and_can_deliver_without_any_registered_browser()
    {
        using var off = await WithDueTaskAsync(subscribed: false);
        off.PushSender.Enqueue(SendOutcome.NoRecipient());
        await off.Planner.PlanAsync();
        await off.Dispatcher.DispatchDueAsync();
        Assert.Empty(off.HaSender.Sent);                                          // opt-in, like ntfy

        using var on = await WithDueTaskAsync(TasksOnly with { HomeAssistantEnabled = true }, subscribed: false);
        on.PushSender.Enqueue(SendOutcome.NoRecipient());
        await on.Planner.PlanAsync();
        await on.Dispatcher.DispatchDueAsync();

        var payload = Assert.Single(on.HaSender.Sent);
        Assert.DoesNotContain("HEMLIG-TITEL", payload.Title + payload.Body);
        var n = await OnlyAsync(on);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal([NotificationChannel.HomeAssistant], n.Deliveries.Select(d => d.Channel));
    }

    [Fact]
    public async Task Ntfy_alone_delivers_when_no_device_is_registered()
    {
        using var h = await WithDueTaskAsync(TasksOnly with { NtfyEnabled = true }, subscribed: false);
        h.PushSender.Enqueue(SendOutcome.NoRecipient());
        await h.Planner.PlanAsync();

        await h.Dispatcher.DispatchDueAsync();

        var n = await OnlyAsync(h);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal([NotificationChannel.Ntfy], n.Deliveries.Select(d => d.Channel));
    }

    [Fact]
    public async Task With_nowhere_to_send_the_notification_is_suppressed_not_retried()
    {
        using var h = await WithDueTaskAsync(subscribed: false);
        h.PushSender.Enqueue(SendOutcome.NoRecipient());
        await h.Planner.PlanAsync();

        Assert.Equal(0, await h.Dispatcher.DispatchDueAsync());

        var n = await OnlyAsync(h);
        Assert.Equal(NotificationStatus.Suppressed, n.Status);
        Assert.Empty(n.Deliveries);
    }

    [Fact]
    public async Task Failures_are_retried_with_growing_delays_and_then_given_up()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();
        for (var i = 0; i < 4; i++)
        {
            h.PushSender.Enqueue(SendOutcome.Fail("HttpRequestException"));
        }

        await h.Dispatcher.DispatchDueAsync();
        Assert.Equal((NotificationStatus.Pending, 1), ((await OnlyAsync(h)).Status, (await OnlyAsync(h)).AttemptCount));

        await h.Dispatcher.DispatchDueAsync();                    // too early: waits five minutes
        Assert.Single(h.PushSender.Sent);

        h.Clock.Set("2026-10-14T10:06:00Z");
        await h.Dispatcher.DispatchDueAsync();
        Assert.Equal(2, h.PushSender.Sent.Count);

        h.Clock.Set("2026-10-14T10:40:00Z");                       // after the 30 minute delay
        await h.Dispatcher.DispatchDueAsync();
        Assert.Equal(3, h.PushSender.Sent.Count);

        h.Clock.Set("2026-10-14T12:50:00Z");                       // after the two hour delay (still before quiet hours)
        await h.Dispatcher.DispatchDueAsync();

        var n = await OnlyAsync(h);
        Assert.Equal((NotificationStatus.Failed, 4), (n.Status, n.AttemptCount));
        Assert.Equal(4, n.Deliveries.Count(d => d.Status == DeliveryStatus.Failed));
        Assert.All(n.Deliveries, d => Assert.Equal("HttpRequestException", d.Error));

        h.Clock.Set("2026-10-15T10:00:00Z");
        await h.Dispatcher.DispatchDueAsync();
        Assert.Equal(4, h.PushSender.Sent.Count);                  // nothing after giving up
    }

    [Fact]
    public async Task A_sender_that_throws_is_treated_as_a_failure_and_does_not_stop_the_run()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();
        h.PushSender.Enqueue(SendOutcome.Fail("boom"));

        await h.Dispatcher.DispatchDueAsync();

        Assert.Equal(NotificationStatus.Pending, (await OnlyAsync(h)).Status);
    }

    [Fact]
    public async Task Switching_the_category_off_after_planning_suppresses_the_reminder()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();
        await h.Prefs.SetNotificationsAsync(TasksOnly with { TasksEnabled = false });

        await h.Dispatcher.DispatchDueAsync();

        Assert.Empty(h.PushSender.Sent);
        Assert.Equal(NotificationStatus.Suppressed, (await OnlyAsync(h)).Status);
    }

    [Fact]
    public async Task During_quiet_hours_the_reminder_waits_until_they_end()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();
        h.Clock.Set("2026-10-14T21:30:00Z"); // 23:30 local

        await h.Dispatcher.DispatchDueAsync();

        Assert.Empty(h.PushSender.Sent);
        var n = await OnlyAsync(h);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-15T05:00:00Z"), n.ScheduledForUtc); // 07:00 local next morning

        h.Clock.Set("2026-10-15T05:01:00Z");
        Assert.Equal(1, await h.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task A_notification_claimed_by_a_worker_that_died_is_picked_up_after_the_lock_expires()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();
        // Simulate a crash after claiming: the sender hangs forever from the first run's point of view, so mark it claimed directly.
        await using (var db = h.Factory.Create())
        {
            var n = db.ScheduledNotifications.Single();
            n.Status = NotificationStatus.Sending;
            n.LockedUntilUtc = h.Clock.Now.AddMinutes(5);
            n.AttemptCount = 1;
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await h.Dispatcher.DispatchDueAsync());   // still locked: another worker may be sending it
        h.Clock.Set("2026-10-14T10:06:00Z");
        Assert.Equal(1, await h.Dispatcher.DispatchDueAsync());   // lock expired

        var done = await OnlyAsync(h);
        Assert.Equal((NotificationStatus.Sent, 2), (done.Status, done.AttemptCount));
    }

    [Fact]
    public async Task A_test_notification_is_sent_immediately_regardless_of_categories_and_quiet_hours()
    {
        using var h = new ServiceHarness();
        await h.Subscriptions.SaveAsync("https://push.example/abc", "key", "auth", null);
        h.Clock.Set("2026-10-14T21:30:00Z"); // quiet hours, all categories off

        await h.Notifications.SendTestAsync();

        Assert.Single(h.PushSender.Sent);
        Assert.Equal(NotificationStatus.Sent, (await OnlyAsync(h)).Status);
    }

    [Fact]
    public async Task One_owner_never_sees_or_triggers_another_owners_notifications()
    {
        using var h = await WithDueTaskAsync();
        await h.Planner.PlanAsync();
        var other = h.ForUser("user-b");
        await other.Prefs.SetNotificationsAsync(TasksOnly);

        Assert.Empty(await other.Notifications.RecentAsync());
        Assert.Equal(0, await other.Planner.PlanAsync()); // user-b has no due tasks of their own
        Assert.Empty(await other.Subscriptions.ListAsync());
        Assert.Equal(0, await other.Dispatcher.DispatchDueAsync());
        Assert.Empty(h.PushSender.Sent);
    }

    // ---------- subscriptions ----------

    [Theory]
    [InlineData("", "k", "a")]
    [InlineData("https://push.example/x", "", "a")]
    [InlineData("https://push.example/x", "k", " ")]
    [InlineData("http://push.example/x", "k", "a")]
    [InlineData("not a url", "k", "a")]
    public async Task Invalid_subscriptions_are_rejected(string endpoint, string key, string auth)
    {
        using var h = new ServiceHarness();
        var ex = await Assert.ThrowsAsync<PlanningException>(() => h.Subscriptions.SaveAsync(endpoint, key, auth, null));
        Assert.Equal(PlanningException.Codes.InvalidSubscription, ex.Code);
        Assert.Empty(await h.Subscriptions.ListAsync());
    }

    [Fact]
    public async Task Saving_the_same_device_twice_updates_it_and_removing_works()
    {
        using var h = new ServiceHarness();
        await h.Subscriptions.SaveAsync("https://push.example/x", "k1", "a1", "Browser");
        await h.Subscriptions.SaveAsync("https://push.example/x", "k2", "a2", "Browser");

        var list = await h.Subscriptions.ListAsync();
        Assert.Single(list);
        Assert.True(list[0].IsActive);

        await h.Subscriptions.RemoveAsync("https://push.example/x");
        await h.Subscriptions.RemoveAsync("https://push.example/x"); // removing twice is harmless
        Assert.Empty(await h.Subscriptions.ListAsync());
    }
}
