using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Building a seat view by hand, for the checks that have to be <em>handed</em> an offer the rules would
    /// never have produced. The second-opinion legality walk is the only caller: it exists to catch an
    /// enumeration that offered something illegal, so a test of it has to be able to offer one.
    /// </summary>
    public static class SeatViews
    {
        public static SeatView Offering(MatchEngine engine, int seat, params MatchIntent[] legal)
            => new SeatView(
                seat,
                engine.Rules,
                engine.Pacing,
                engine.Timings,
                engine.Model.Seats,
                engine.Model.Stakes,
                engine.BuildHandView(seat),
                engine.BuildPeekView(seat),
                new List<MatchIntent>(legal),
                engine.Pending);
    }
}
