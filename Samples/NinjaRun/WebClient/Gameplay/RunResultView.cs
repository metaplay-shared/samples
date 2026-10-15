using Game.Logic;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace WebClient.Gameplay;

/// <summary>
/// How a run's outcome is written on the score screen and the segment callout over the run: which placements
/// it shows, the field in the order it came home, the colours and labels a placement carries, and the words
/// for a run the server would not count. Pure functions of the run and the player's model.
/// </summary>
public static class RunResultView
{
    /// <summary>Par on the standing's axis, in permille of the par time.</summary>
    public const int ParPermille = 1000;

    /// <summary>
    /// Whether the score screen lists the field the run was raced against, which a rejected run does not.
    /// </summary>
    public static bool ShowField(TimeTrial trial)
        => trial != null && trial.Lineup.Count > 0 && trial.Phase != TimeTrialPhase.Rejected;

    /// <summary>
    /// Whether the run shows its segments' placements: a run with a field that still stands, unless it is
    /// practice, which is placed nowhere and pays nothing.
    /// </summary>
    public static bool ShowPlacement(TimeTrial trial) => ShowField(trial) && !trial.IsPractice;

    /// <summary>
    /// Where the run came in on each segment. Once the server has accepted the run these are the placements
    /// it computed; until then they are the browser's own comparison against the same lineup, shown as
    /// provisional.
    /// </summary>
    public static IReadOnlyList<int> SegmentPlacements(TimeTrial trial, PlayerModel player)
    {
        if (trial == null)
            return Array.Empty<int>();
        if (trial.Phase == TimeTrialPhase.Accepted && player != null)
            return player.LastSegmentPlacements;
        return trial.ProvisionalSegmentPlacements;
    }

    /// <summary>
    /// Whether the placements on screen are still the browser's own. They are replaced by the ones the
    /// server computed from its own run of the recording the moment it accepts it.
    /// </summary>
    public static bool ArePlacementsProvisional(TimeTrial trial) => trial?.Phase != TimeTrialPhase.Accepted;

    /// <summary>Where the run came in on one segment, or zero for a segment it has not closed.</summary>
    public static int PlacementForSegment(TimeTrial trial, PlayerModel player, int segment)
    {
        IReadOnlyList<int> placements = SegmentPlacements(trial, player);
        return segment >= 0 && segment < placements.Count ? placements[segment] : 0;
    }

    /// <summary>
    /// What one placement pays, from the published Orb table. A segment that was never placed pays nothing,
    /// which is the same as coming last.
    /// </summary>
    public static int OrbsForPlacement(PlayerModel player, int placement) => player?.GetOrbsForPlacement(placement) ?? 0;

    /// <summary>
    /// How large a segment's gate is drawn: the winner's is the biggest and the rest step down from it, so
    /// the row of gates reads as a result before any of them is looked at.
    /// </summary>
    public static string GateSymbolSizeCssClass(int placement) => placement switch
    {
        1 => "gate-symbol-first",
        2 => "gate-symbol-second",
        3 => "gate-symbol-third",
        _ => "gate-symbol-unlit",
    };

    /// <summary>A placement as it is written on a gate. An unreached gate shows a dash.</summary>
    public static string PlacementLabel(int placement) => placement switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        4 => "4th",
        _ => "—",
    };

    /// <summary>
    /// The colour a placement carries (gold, silver, bronze, then grey) wherever it is written: the score
    /// screen's gates and field, and the segment callout over the run.
    /// </summary>
    public static string PlacementCssClass(int placement) => placement switch
    {
        1 => "placement-first",
        2 => "placement-second",
        3 => "placement-third",
        4 => "placement-fourth",
        _ => "placement-open",
    };

    /// <summary>
    /// How the field came home: the player and the opponents ordered by finish time, each with the place that
    /// time earned. Equal times share the better place. Empty until the run is over, and the opponents are
    /// included only where <see cref="ShowField"/> lists them.
    /// </summary>
    public static List<(int Place, string Name, int TimeMs, bool IsPlayer)> FinishOrder(TimeTrial trial)
    {
        List<(int Place, string Name, int TimeMs, bool IsPlayer)> order = new List<(int, string, int, bool)>();
        if (trial == null || trial.Phase == TimeTrialPhase.Ready || trial.Phase == TimeTrialPhase.Running)
            return order;

        List<(string Name, int TimeMs, bool IsPlayer)> runners = new List<(string, int, bool)>();
        runners.Add(("You", DisplayTimeMs(trial), true));
        if (ShowField(trial))
        {
            foreach (TimeTrialGhost opponent in trial.Lineup)
                runners.Add((opponent.DisplayName ?? TimeTrialSkills.MaskedNinjaName, opponent.FinishTimeMs, false));
        }

        runners.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));

        for (int ndx = 0; ndx < runners.Count; ndx++)
        {
            int place = 1;
            for (int other = 0; other < runners.Count; other++)
            {
                if (runners[other].TimeMs < runners[ndx].TimeMs)
                    place++;
            }

            order.Add((place, runners[ndx].Name, runners[ndx].TimeMs, runners[ndx].IsPlayer));
        }

        return order;
    }

    /// <summary>
    /// The player's time as the score screen writes it: the run's own finish time, then the one the server
    /// computed once it has answered. A run that used its whole step budget without finishing shows the clock
    /// it stopped at.
    /// </summary>
    public static int DisplayTimeMs(TimeTrial trial)
    {
        if (trial == null)
            return 0;
        if (trial.AcceptedResult != null)
            return trial.AcceptedResult.FinishTimeMs;
        if (trial.ProvisionalResult != null)
            return trial.ProvisionalResult.FinishTimeMs;
        return trial.ElapsedMs;
    }

    /// <summary>
    /// The one line the score screen has to say in words, or null when there is nothing to explain: why the
    /// server would not count the run, or that it was practice and nothing about it is kept. A run on its way
    /// to the server says so through the provisional marker on the gates instead.
    /// </summary>
    public static string? StatusText(TimeTrial trial)
    {
        if (trial == null)
            return null;
        if (trial.Phase == TimeTrialPhase.Rejected)
            return $"Rejected: {DescribeRejection(trial.RejectReason)}";
        if (trial.IsPractice)
            return "Practice — nothing is recorded";

        return null;
    }

    /// <summary>
    /// The standing the score screen's standing panel draws, or null when it has none: only an accepted run
    /// whose course sent back its players' bests has one.
    /// </summary>
    public static TimeTrialStanding? StandingFor(TimeTrial trial)
    {
        TimeTrialStanding? standing = trial?.Standing;
        bool drawable = trial?.Phase == TimeTrialPhase.Accepted
            && standing?.PlayerBestCounts != null
            && standing.PlayerBestCounts.Length == CourseTimeBuckets.BucketCount;
        return drawable ? standing : null;
    }

    /// <summary>
    /// A bar's height as a share of the tallest, for its <c>--bar-height</c>. With every bar empty they are all
    /// 0, and css/score.css keeps a stub of each so the axis still reads.
    /// </summary>
    public static string BarHeight(int count, int maxCount)
        => (maxCount > 0 ? (double)count / maxCount : 0.0).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Where on the standing's axis <paramref name="permille"/> of par falls, as a CSS left offset, or null
    /// when no bar starts there. Every bar is the same width, so a bar's left edge is its index over the bar
    /// count.
    /// </summary>
    public static string? AxisOffset(IReadOnlyList<CourseTimeHistogramBar> bars, int permille)
    {
        for (int ndx = 0; ndx < bars.Count; ndx++)
        {
            if (!bars[ndx].IsOpenEnd && bars[ndx].LowerEdgePermille == permille)
                return $"{(ndx * 100.0 / bars.Count).ToString("0.###", CultureInfo.InvariantCulture)}%";
        }
        return null;
    }

    /// <summary>What to tell the player about a submission the server turned down.</summary>
    public static string DescribeRejection(TimeTrialRejectReason reason) => reason switch
    {
        TimeTrialRejectReason.NoActiveTicket     => "there was no run open",
        TimeTrialRejectReason.TicketMismatch     => "a newer run had replaced this one",
        TimeTrialRejectReason.TicketExpired      => "the run was open for too long",
        TimeTrialRejectReason.UnsupportedRuleset => "the game's rules have changed since the run started",
        TimeTrialRejectReason.UnknownCourse      => "the course is no longer available",
        TimeTrialRejectReason.MalformedRecording => "the run could not be read",
        TimeTrialRejectReason.DidNotFinish       => "the run never reached the finish line",
        _                                        => "the run was not counted",
    };
}
