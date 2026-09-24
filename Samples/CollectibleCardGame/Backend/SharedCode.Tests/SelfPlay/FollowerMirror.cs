using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Logic.Tests
{
    /// <summary> A divergence between the authority and its follower. Carries the diff, not just the fact. </summary>
    public class FollowerDivergence : Exception
    {
        public FollowerDivergence(string message) : base(message) { }
    }

    /// <summary>
    /// <b>The follower, offline.</b> A clone of the authoritative model taken through the SDK's own network
    /// mask, fed exactly the actions a real client would receive, and compared after every one.
    /// <para>
    /// This is the refactor's enforcement mechanism, and it is what makes "an action's public mutation comes
    /// from the payload and public state only" a property rather than a convention. It is not a simulation of the SDK's replication: it uses the same two masks the SDK uses
    /// and runs the same <c>Execute</c> body the SDK runs. What it does not reproduce is the transport and
    /// the frame pump, neither of which can change what an action does to a model.
    /// </para>
    /// <para>
    /// The clone is made <b>once per game, at the deal</b>, and every action of the game is applied to it —
    /// which is the honest simulation, because a real follower subscribes once and replays everything after
    /// that. Re-cloning per action would test nothing about accumulated state, which is exactly where a
    /// forgotten stored count shows up.
    /// </para>
    /// </summary>
    public sealed class FollowerMirror
    {
        readonly MatchModel        _follower;
        readonly RecordingListener _listener;

        FollowerMirror(MatchModel follower, RecordingListener listener)
        {
            _follower = follower;
            _listener = listener;
        }

        /// <summary> How many actions this mirror has been fed, for the harness to report. </summary>
        public int ActionsApplied { get; private set; }

        /// <summary>
        /// Every action type any mirror in this test run has actually executed against a follower.
        /// <para>
        /// This exists so the coverage can be asserted rather than assumed, and it is assumed easily: the
        /// first version of this harness mirrored five of fifteen actions and read as though it mirrored all
        /// of them, because every mirrored game was dealt at zero timings and the actions that only exist
        /// under a clock — a released beat, a lapsed mulligan, a reserve extension — were unreachable.
        /// <see cref="FollowerMirrorCoverageTests"/> turns this set into the ratchet: a sixteenth action
        /// cannot join the block without a mirrored path that runs it.
        /// </para>
        /// </summary>
        static readonly HashSet<Type> Applied = new HashSet<Type>();

        /// <summary> Whether any mirror has run this action type against a follower yet. </summary>
        public static bool HasApplied(Type actionType)
        {
            lock (Applied)
                return Applied.Contains(actionType);
        }

        static string Roster(MatchModel model, int seat)
        {
            if (model.Seats == null || seat >= model.Seats.Count)
                return "none";

            MatchSeat roster = model.Seats[seat];
            return $"{roster.DisplayName}/{roster.Occupancy}/{(roster.IsConnected ? "here" : "away")}/{roster.Strikes}";
        }

        static string Acked(MatchModel model, int seat)
            => model.ResultAcked != null && seat < model.ResultAcked.Count ? model.ResultAcked[seat].ToString() : "none";

        static string Grace(MatchModel model, int seat)
            => model.Pacing?.Grace(seat)?.ToString() ?? "none";

        /// <summary> The recorded set, copied, so a reader cannot race the writer. </summary>
        public static List<Type> AppliedActionTypes()
        {
            lock (Applied)
                return new List<Type>(Applied);
        }

        /// <summary>
        /// A follower of this model, as the SDK would hand one to a subscriber:
        /// <c>SendOverNetwork</c>-serialized, deserialized against the entity's own game config, and given a
        /// listener so its recorded event stream can be compared.
        /// </summary>
        /// <summary>
        /// The seat this follower is a client of. A real client receives addressed operations for its own seat
        /// and no other — the SDK gives every other member a no-op — so the mirror models one seat rather than
        /// pretending to be both. The wrong-seat refusal is asserted on its own, in MatchMessageTests.
        /// </summary>
        public const int MirroredSeat = 0;

        public static FollowerMirror Of(MatchModel authoritative, SharedGameConfig config)
        {
            MatchModel follower = MetaSerialization.DeserializeTagged<MatchModel>(
                MetaSerialization.SerializeTagged(authoritative, MetaSerializationFlags.SendOverNetwork, logicVersion: null),
                MetaSerializationFlags.SendOverNetwork,
                resolver: config,
                logicVersion: null);

            // What DefaultDeserializeModel does before the model becomes the journal's staged copy.
            follower.GameConfig = config;

            // The interpreter is a hook rather than state, so it does not travel; a follower resolves the
            // same steps with the same implementation the authority used.
            follower.Interpreter = authoritative.Interpreter;

            // What GetMemberPrivateState hands a subscribing client: the baseline for its own hand. The model
            // is deserialized under the network mask first, which is why this has to be put back by hand — the
            // client's own view is not on the wire at all.
            // Everything after it arrives as an addressed change, and OwnHandMatchesTheAuthority() checks,
            // after every action of every game, that replaying those changes reproduces the real hand.
            follower.OwnHand = SecretOps.HandOf(authoritative, MirroredSeat);
            follower.OwnPeek = HandViews.BuildPendingChoice(authoritative, MirroredSeat);

            RecordingListener listener = new RecordingListener();
            follower.ClientListener = listener;

            return new FollowerMirror(follower, listener);
        }

        /// <summary>
        /// Give this follower different durations from its authority, as it would have if
        /// <see cref="MatchTimings"/> were not a replicated member. Test-only, and the subject of a negative
        /// control rather than a thing any real follower can be.
        /// </summary>
        public void MakeTimingsDisagree(MatchTimings timings) => _follower.Timings = timings;

        /// <summary>
        /// One tick of the model's clock, which a real follower plays off the timeline like any action. The
        /// next comparison covers the clock, because the tick counter is checksummed.
        /// </summary>
        public void Tick() => _follower.Tick(checksumCtx: null);

        /// <summary>
        /// Whether seat 0's hand, as this follower has maintained it from addressed changes alone, still says
        /// exactly what the authority's secret hand says — same cards, same ranks, same order.
        /// <para>
        /// This is the property the delta design rests on, and it is checked on every action of every game
        /// rather than in a fixture: a dropped change, a change applied twice, or one applied out of order all
        /// show up here, and none of them would move a checksum.
        /// </para>
        /// </summary>
        public string OwnHandDivergence(MatchModel authoritative)
        {
            List<HandCard>     truth = SecretOps.HandOf(authoritative, MirroredSeat);
            List<HandCard> mine  = _follower.OwnHand;

            if (truth.Count != mine.Count)
                return $"seat {MirroredSeat} holds {truth.Count} cards, the follower's own view holds {mine.Count}";

            for (int ndx = 0; ndx < truth.Count; ndx++)
            {
                if (truth[ndx].Instance != mine[ndx].Instance || truth[ndx].Card != mine[ndx].Card || truth[ndx].Rank != mine[ndx].Rank)
                    return $"seat {MirroredSeat}'s card {ndx} is {truth[ndx]}, the follower's own view says {mine[ndx].Instance} {mine[ndx].Card} r{mine[ndx].Rank}";
            }

            return null;
        }

        /// <summary>
        /// Run one action the way a follower runs it, then compare.
        /// <para>
        /// The action object itself is round-tripped through the network mask first, because that is what a
        /// follower actually holds — and it is what would strip a <c>[ServerOnly]</c> member off a payload if
        /// one ever appeared there.
        /// </para>
        /// </summary>
        public void Apply(MatchAction action, MatchModel authoritative, IReadOnlyList<MatchEvent> expectedEvents)
        {
            MatchAction received = MetaSerialization.CloneTagged(action, MetaSerializationFlags.SendOverNetwork, logicVersion: null, resolver: _follower.Content);

            _listener.Clear();

            Metaplay.Core.Model.MetaActionResult result;
            try
            {
                result = received.InvokeExecute(_follower, commit: true);
            }
            catch (Exception ex)
            {
                // A rule body that read hidden state on a follower throws here. The SDK would close that
                // client's session; the harness reports it as the divergence it is rather than swallowing it.
                throw new FollowerDivergence(
                    $"the follower threw running {action.GetType().Name}: {ex.GetType().Name}: {ex.Message}");
            }

            // A follower that refuses an action the leader committed silently does not apply it, and the
            // divergence surfaces a checksum period later with nothing naming the cause. Throwing turns that
            // into a named failure.
            if (!result.IsSuccess)
                throw new FollowerDivergence($"the follower refused {action.GetType().Name}: {result}");

            ActionsApplied++;
            lock (Applied)
                Applied.Add(action.GetType());

            string diff = Compare(authoritative, expectedEvents);
            if (diff != null)
                throw new FollowerDivergence($"after {action.GetType().Name}: {diff}");
        }

        /// <summary>
        /// Null when the two models agree, a diff when they do not. Two axes, and both are needed:
        /// <list type="number">
        /// <item><b>Equal checksum bytes.</b> Exactly what the SDK hashes, so equal bytes is strictly
        /// stronger than an equal hash and gives a diffable failure instead of "two numbers differ".</item>
        /// <item><b>Equal recorded public events.</b> What catches an event whose <em>content</em> differs
        /// even though the state agrees — a played-card event naming a different card, say, which no state
        /// comparison can see.</item>
        /// </list>
        /// </summary>
        public string Compare(MatchModel authoritative, IReadOnlyList<MatchEvent> expectedEvents)
        {
            byte[] mine   = Checksummed(_follower);
            byte[] theirs = Checksummed(authoritative);

            if (!Same(mine, theirs))
                return "checksummed state differs; " + NameTheMember(authoritative);

            if (expectedEvents != null)
            {
                IReadOnlyList<MatchEvent> recorded = _listener.Events;
                if (recorded.Count != expectedEvents.Count)
                    return $"the follower recorded {recorded.Count} event(s) where the authority recorded {expectedEvents.Count}";

                for (int ndx = 0; ndx < recorded.Count; ndx++)
                {
                    if (!Same(EventBytes(recorded[ndx]), EventBytes(expectedEvents[ndx])))
                        return $"event {ndx} differs: the follower recorded {recorded[ndx].GetType().Name} '{recorded[ndx]}', the authority '{expectedEvents[ndx]}'";
                }
            }

            return null;
        }

        /// <summary>
        /// Which member the divergence is in, by serializing each of the model's public members on its own.
        /// The failure is far more useful with a name on it, and there are three likely answers: a count means a forgotten public mutation in the zone ops, a pacing stamp means one
        /// derived from something other than the payload's clock and the timings, and the registry means a
        /// reveal that did not come from a payload.
        /// </summary>
        string NameTheMember(MatchModel authoritative)
        {
            StringBuilder differing = new StringBuilder();

            Check(differing, "Rules.Turn",        authoritative.Rules.Turn.ToString(),        _follower.Rules.Turn.ToString());
            Check(differing, "Rules.ActionCount", authoritative.Rules.ActionCount.ToString(), _follower.Rules.ActionCount.ToString());
            Check(differing, "Rules.Phase",       authoritative.Rules.Phase.ToString(),       _follower.Rules.Phase.ToString());
            Check(differing, "Rules.SeatOnTurn",  authoritative.Rules.SeatOnTurn.ToString(), _follower.Rules.SeatOnTurn.ToString());

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState mine   = _follower.Rules.Seat(seat);
                SeatState theirs = authoritative.Rules.Seat(seat);

                Check(differing, $"Seat{seat}.HandCount",    theirs.HandCount.ToString(),    mine.HandCount.ToString());
                Check(differing, $"Seat{seat}.DeckCount",    theirs.DeckCount.ToString(),    mine.DeckCount.ToString());
                Check(differing, $"Seat{seat}.DenHp",        theirs.DenHp.ToString(),        mine.DenHp.ToString());
                Check(differing, $"Seat{seat}.Mana",         theirs.Mana.ToString(),         mine.Mana.ToString());
                Check(differing, $"Seat{seat}.Board",        theirs.Board.Count.ToString(),  mine.Board.Count.ToString());
                Check(differing, $"Seat{seat}.Graveyard",    theirs.Graveyard.Count.ToString(), mine.Graveyard.Count.ToString());
                Check(differing, $"Seat{seat}.UnseenPool",   theirs.UnseenPool.Count.ToString(), mine.UnseenPool.Count.ToString());
                Check(differing, $"Seat{seat}.Tuckered",     theirs.TuckeredOutTicks.ToString(), mine.TuckeredOutTicks.ToString());

                // The table's own per-seat members, which the actor's table actions write. Without these a
                // break planted in MatchSetSeats, MatchSetGrace or MatchSetResultAck is caught and then
                // reported as "not one this summary names".
                Check(differing, $"Seat{seat}.Roster",       Roster(authoritative, seat),        Roster(_follower, seat));
                Check(differing, $"Seat{seat}.ResultAcked",  Acked(authoritative, seat),         Acked(_follower, seat));
                Check(differing, $"Seat{seat}.Grace",        Grace(authoritative, seat),         Grace(_follower, seat));
            }

            Check(differing, "Rules.Instances",    authoritative.Rules.Instances.Count.ToString(),   _follower.Rules.Instances.Count.ToString());
            Check(differing, "Rules.EffectQueue",  authoritative.Rules.EffectQueue.Count.ToString(), _follower.Rules.EffectQueue.Count.ToString());
            Check(differing, "Pacing.DeadlineAt",  authoritative.Pacing.DeadlineAt?.ToString() ?? "none", _follower.Pacing.DeadlineAt?.ToString() ?? "none");
            Check(differing, "Pacing.Suspended",   authoritative.Pacing.SuspendedDeadlineAt?.ToString() ?? "none", _follower.Pacing.SuspendedDeadlineAt?.ToString() ?? "none");
            Check(differing, "History.Count",      authoritative.History.Count.ToString(),           _follower.History.Count.ToString());

            // The table's own state, written by the actor's own actions rather than by a rule. Phase is here
            // because it was the member a planted break in MatchSetPhase moved, and the summary sent the
            // reader to the card registry instead.
            Check(differing, "Phase",              authoritative.Phase.ToString(),                   _follower.Phase.ToString());
            Check(differing, "Result",             authoritative.Result?.Outcome.ToString() ?? "none", _follower.Result?.Outcome.ToString() ?? "none");

            // Nothing above caught it, so it is inside a member the summary does not name — the registry's
            // revealed identities are the usual answer, which is a reveal that did not come from a payload.
            return differing.Length > 0
                ? differing.ToString()
                : "the differing member is not one this summary names; compare Rules.Instances' revealed identities";
        }

        static void Check(StringBuilder into, string name, string authority, string follower)
        {
            if (authority != follower)
                into.Append($"{name}: authority '{authority}', follower '{follower}'. ");
        }

        /// <summary> Exactly the bytes the SDK hashes for a checksum. </summary>
        static byte[] Checksummed(MatchModel model)
            => MetaSerialization.SerializeTagged(model, MetaSerializationFlags.ComputeChecksum, logicVersion: null);

        static byte[] EventBytes(MatchEvent ev)
            => MetaSerialization.SerializeTagged(ev, MetaSerializationFlags.SendOverNetwork, logicVersion: null);

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int ndx = 0; ndx < a.Length; ndx++)
            {
                if (a[ndx] != b[ndx])
                    return false;
            }

            return true;
        }

        /// <summary> The client listener a follower would have: it collects what the actions told it. </summary>
        sealed class RecordingListener : IMatchModelClientListener
        {
            readonly List<MatchEvent> _events = new List<MatchEvent>();

            public IReadOnlyList<MatchEvent> Events => _events;

            public void Clear() => _events.Clear();

            public void OnMatchEvent(MatchEvent ev) => _events.Add(ev);
            public void OnBoardChanged() { }
            public void OnSeatsChanged() { }
            public void OnPacingChanged() { }
            public void OnPhaseChanged(MatchTablePhase phase) { }
            public void OnResult(MatchOutcomeRecord result) { }
            public void OnResultAcked(int seat) { }
        }
    }
}
