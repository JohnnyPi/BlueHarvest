using BlueHarvest.Sim.Time;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class SchedulerTests
{
    [Fact]
    public void EarlierTickComesFirst()
    {
        var scheduler = new Scheduler();
        scheduler.Schedule(1, 50);
        scheduler.Schedule(2, 10);

        Assert.True(scheduler.TryDequeue(out int id, out long tick));
        Assert.Equal(2, id);
        Assert.Equal(10, tick);
    }

    [Fact]
    public void SameTick_KeepsInsertionOrder()
    {
        var scheduler = new Scheduler();
        scheduler.Schedule(3, 5);
        scheduler.Schedule(1, 5);
        scheduler.Schedule(2, 5);

        var order = new List<int>();
        while (scheduler.TryDequeue(out int id, out _))
            order.Add(id);

        Assert.Equal(new[] { 3, 1, 2 }, order);
    }
}
