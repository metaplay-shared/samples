using Game.Client.Services;
using Metaplay.Core;
using System;

namespace Game.Client.Tests;

/// <summary> The model's clock between ticks, away from the browser. </summary>
[TestFixture]
public class ModelClockTests
{
    static readonly MetaTime Model = MetaTime.FromDateTime(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc));
    static readonly MetaTime Shown = MetaTime.FromDateTime(new DateTime(2026, 9, 9, 11, 47, 0, DateTimeKind.Utc));

    static MetaTime After(MetaTime at, long milliseconds) => at + MetaDuration.FromMilliseconds(milliseconds);

    [Test]
    public void AtTheTickItReadsTheTick()
    {
        Assert.That(ModelClock.Interpolate(Model, Shown, Shown, ticksPerSecond: 1), Is.EqualTo(Model));
    }

    [Test]
    public void BetweenTicksItCarriesTheClockForward()
    {
        Assert.That(ModelClock.Interpolate(Model, Shown, After(Shown, 400), ticksPerSecond: 1), Is.EqualTo(After(Model, 400)));
    }

    [Test]
    public void ItNeverReadsPastTheNextTick()
    {
        // A timeline that stopped delivering is a clock that stopped: a countdown must not keep draining past
        // what the server has said.
        Assert.That(ModelClock.Interpolate(Model, Shown, After(Shown, 5_000), ticksPerSecond: 1), Is.EqualTo(After(Model, 1_000)));
        Assert.That(ModelClock.Interpolate(Model, Shown, After(Shown, 5_000), ticksPerSecond: 10), Is.EqualTo(After(Model, 100)));
    }

    [Test]
    public void ADeviceClockThatStepsBackReadsTheTick()
    {
        Assert.That(ModelClock.Interpolate(Model, Shown, After(Shown, -30_000), ticksPerSecond: 1), Is.EqualTo(Model));
    }

    [Test]
    public void OnlyElapsedDeviceTimeMatters()
    {
        // The device clock here is thirteen minutes behind the model's; a device an hour further off reads the
        // same, because only the time since the tick was presented enters the answer.
        MetaTime shownAnHourOff = After(Shown, -3_600_000);
        Assert.That(ModelClock.Interpolate(Model, shownAnHourOff, After(shownAnHourOff, 250), ticksPerSecond: 1),
            Is.EqualTo(ModelClock.Interpolate(Model, Shown, After(Shown, 250), ticksPerSecond: 1)));
    }
}
