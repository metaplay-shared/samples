using Metaplay.Core;

namespace Game.Client.Services;

/// <summary>
/// The model's clock between ticks. A client's copy of the model's time moves only when a tick is played off
/// the timeline, a second at a time, so a countdown read straight off it would step. This carries it forward
/// by the device time elapsed since that tick was presented, capped at one tick: a ring drains smoothly, stops
/// where the timeline stops, and never reads ahead of the next tick. Only elapsed device time is read, never
/// its absolute value, so a device clock that is wrong by minutes changes nothing.
/// <para>
/// Pure, and free of anything browser-shaped so it is unit-tested away from the browser.
/// </para>
/// </summary>
public static class ModelClock
{
    /// <summary>
    /// The model's time as of <paramref name="deviceNow"/>, given the model's time at its latest tick and the
    /// device time that tick was presented at.
    /// </summary>
    public static MetaTime Interpolate(MetaTime modelTime, MetaTime tickPresentedAt, MetaTime deviceNow, int ticksPerSecond)
    {
        MetaDuration tick      = MetaDuration.FromMilliseconds(1000 / ticksPerSecond);
        MetaDuration sinceTick = deviceNow - tickPresentedAt;

        if (sinceTick < MetaDuration.Zero)
            return modelTime;
        if (sinceTick > tick)
            return modelTime + tick;

        return modelTime + sinceTick;
    }
}
