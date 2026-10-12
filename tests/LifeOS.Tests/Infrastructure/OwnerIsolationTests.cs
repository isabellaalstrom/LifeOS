using LifeOS.Domain.LifeAreas;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests.Infrastructure;

public class OwnerIsolationTests
{
    [Fact]
    public async Task Owner_only_sees_their_own_rows()
    {
        using var db = new TestDatabase();
        await using (var a = db.CreateContext(new FakeCurrentUser("user-a")))
        {
            a.LifeAreas.Add(new LifeArea { Name = "Hälsa" });
            await a.SaveChangesAsync();
        }

        await using (var b = db.CreateContext(new FakeCurrentUser("user-b")))
        {
            b.LifeAreas.Add(new LifeArea { Name = "Cykling" });
            await b.SaveChangesAsync();
        }

        await using var readA = db.CreateContext(new FakeCurrentUser("user-a"));
        Assert.Equal(["Hälsa"], await readA.LifeAreas.Select(x => x.Name).ToListAsync());

        await using var readB = db.CreateContext(new FakeCurrentUser("user-b"));
        Assert.Equal(["Cykling"], await readB.LifeAreas.Select(x => x.Name).ToListAsync());
    }

    [Fact]
    public async Task Anonymous_sees_nothing_and_cannot_write()
    {
        using var db = new TestDatabase();
        await using (var owner = db.CreateContext(new FakeCurrentUser("user-a")))
        {
            owner.LifeAreas.Add(new LifeArea { Name = "Hem" });
            await owner.SaveChangesAsync();
        }

        await using var anonymous = db.CreateContext(new FakeCurrentUser(null));
        Assert.Empty(await anonymous.LifeAreas.ToListAsync());

        anonymous.LifeAreas.Add(new LifeArea { Name = "Intrång" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => anonymous.SaveChangesAsync());
    }

    [Fact]
    public async Task Owner_and_timestamps_are_assigned_on_insert_and_owner_cannot_change()
    {
        using var db = new TestDatabase();
        await using var context = db.CreateContext(new FakeCurrentUser("user-a"));
        var area = new LifeArea { Name = "Hem", OwnerId = "spoofed" };
        context.LifeAreas.Add(area);
        await context.SaveChangesAsync();

        Assert.Equal("user-a", area.OwnerId);
        Assert.NotEqual(default, area.CreatedUtc);

        area.OwnerId = "user-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Timestamps_round_trip_in_utc_and_sort_chronologically()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateContext(new FakeCurrentUser("user-a")))
        {
            context.LifeAreas.AddRange(new LifeArea { Name = "först" }, new LifeArea { Name = "sist" });
            await context.SaveChangesAsync();
        }

        await using var read = db.CreateContext(new FakeCurrentUser("user-a"));
        var ordered = await read.LifeAreas.OrderByDescending(x => x.CreatedUtc).ToListAsync();
        Assert.Equal(2, ordered.Count);
        Assert.All(ordered, x => Assert.Equal(TimeSpan.Zero, x.CreatedUtc.Offset));
    }
}
