using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// How a finished match converts to weekly-event points, and the limits an authored week must stay within
    /// (<c>docs/weekly-event.md</c>).
    /// <para>
    /// <b>The scoring rule is in code, not config.</b> Points per trick plus a win bonus is shared by the
    /// server, the client and stored player state, so a config publish must not change it during a running
    /// week. The target and the bonus size are content on <see cref="WeeklyEventContent"/>.
    /// </para>
    /// </summary>
    public static class WeeklyEventScoring
    {
        /// <summary>Points per trick taken.</summary>
        public const int PointsPerTrick = 1;

        /// <summary>The allowed range of a week's win bonus.</summary>
        public const int MinWinBonusPoints = 1;
        public const int MaxWinBonusPoints = 10;

        /// <summary>
        /// The maximum target, in perfect matches: a target may not exceed <see cref="MaxPointsPerMatch"/> times
        /// this value. It keeps every week finishable and limits the list of counted match IDs
        /// (<c>docs/player.md</c>, "Observer rules"). Each recorded match scores at least one point and nothing is
        /// recorded after the target is reached, so the list holds at most one ID per target point. The weekly event
        /// has no other limit on matches, so without this check the list could grow with every match played.
        /// </summary>
        public const int MaxTargetInPerfectMatches = 60;

        /// <summary>
        /// The minimum target of an authored week, in perfect matches, so a week cannot be finished in one session.
        /// It is a balance rule, so only the config build checks it (<see cref="WeeklyEventTemplateInfo.Validate"/>).
        /// <see cref="ProblemsWith"/> does not check it, because event creation refuses only events that would not
        /// work, and the end-to-end test creates an event with a target that one match can reach.
        /// </summary>
        public const int MinTargetInPerfectMatches = 5;

        /// <summary>
        /// The balance checks an <b>authored</b> week must pass in addition to <see cref="ProblemsWith"/>: the
        /// target must need at least <see cref="MinTargetInPerfectMatches"/> perfect matches. Kept separate from
        /// <see cref="ProblemsWith"/> for the reason given on <see cref="MinTargetInPerfectMatches"/>.
        /// </summary>
        public static IEnumerable<string> BalanceProblemsWith(WeeklyEventContent content)
        {
            if (content == null || content.TargetPoints <= 0)
                yield break;

            int perMatch = MaxPointsPerMatch(content.WinBonusPoints);
            if (perMatch > 0 && content.TargetPoints < perMatch * MinTargetInPerfectMatches)
                yield return $"asks for {content.TargetPoints} points, fewer than the {perMatch * MinTargetInPerfectMatches} a player scores in {MinTargetInPerfectMatches} perfect matches";
        }

        /// <summary>The most points one match can score in a week whose win bonus is <paramref name="winBonusPoints"/>.</summary>
        public static int MaxPointsPerMatch(int winBonusPoints) => MatchRules.NumTricks * PointsPerTrick + winBonusPoints;

        /// <summary>
        /// The points a finished match scores in a week: <see cref="PointsPerTrick"/> per trick taken, plus the
        /// week's win bonus if the player won. Both inputs are already in <see cref="MatchCompletion"/>.
        /// </summary>
        public static int PointsFor(in MatchCompletion completion, WeeklyEventContent content)
        {
            if (content == null)
                return 0;

            int tricks = completion.TricksWon > 0 ? completion.TricksWon : 0;
            return tricks * PointsPerTrick + (completion.IsWin ? content.WinBonusPoints : 0);
        }

        /// <summary>
        /// Returns the problems that would make a week <b>not work</b>: no theme, no reward, an unreachable
        /// target, a win bonus out of range, or a reward currency this feature must not grant.
        /// <para>
        /// Both the config build (for every template) and the SDK's event creation check (for content that may
        /// not come from a template) run these checks. It intentionally excludes the config build's balance
        /// minimum (<see cref="MinTargetInPerfectMatches"/>) and wallet cap check, which apply only to authored
        /// content. <see cref="WeeklyEventTemplateInfo.Validate"/> runs both sets.
        /// </para>
        /// </summary>
        public static IEnumerable<string> ProblemsWith(WeeklyEventContent content)
        {
            if (content == null)
            {
                yield return "has no content";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(content.Theme))
                yield return "has no theme name for the card to carry";
            if (string.IsNullOrWhiteSpace(content.Tagline))
                yield return "has no tagline saying what the week is about";

            if (content.WinBonusPoints < MinWinBonusPoints || content.WinBonusPoints > MaxWinBonusPoints)
                yield return $"pays {content.WinBonusPoints} points for a win, outside the {MinWinBonusPoints}-{MaxWinBonusPoints} band";

            if (content.TargetPoints <= 0)
            {
                yield return $"asks for {content.TargetPoints} points, which is not a target";
            }
            else
            {
                int perMatch = MaxPointsPerMatch(content.WinBonusPoints);
                if (perMatch > 0 && content.TargetPoints > perMatch * MaxTargetInPerfectMatches)
                    yield return $"asks for {content.TargetPoints} points, more than the {perMatch * MaxTargetInPerfectMatches} a player can score in {MaxTargetInPerfectMatches} perfect matches";
            }

            if (content.Reward == null || content.Reward.Amounts == null || content.Reward.Amounts.Count == 0)
                yield return "grants nothing for crossing the target";
            else if (content.Reward.Grants(CurrencyType.SpinTokens))
                yield return "pays spin tokens; the wheel's two faucets are the daily reward and the weekly mission set, and a third would make its input unattributable";
        }
    }
}
