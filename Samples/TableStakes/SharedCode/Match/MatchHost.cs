using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Game.Logic
{
    /// <summary>
    /// The host-specific operations that <see cref="MatchHost"/> needs: publishing an action to the timeline,
    /// drawing host random seeds, and notifying one seat's client.
    /// <para>
    /// The server's match actor publishes with <c>ExecuteAction</c> and sends to a session. The browser's offline
    /// server publishes to the offline timeline and sends on the entity channel. <see cref="MatchHost"/> makes the
    /// same decisions for both.
    /// </para>
    /// </summary>
    public interface IMatchHostEnvironment
    {
        /// <summary>
        /// Put an action on the timeline and return whether the timeline accepted it. <see cref="MatchHost"/>
        /// commits the engine only when this returns true. Returning true for a rejected action would leave the
        /// server-only engine one play ahead of the board, and no checksum would detect it (<c>docs/match.md</c>).
        /// </summary>
        bool Publish(MatchAction action);

        /// <summary>A new random seed for host decisions: which card a bot plays and its think delay.</summary>
        ulong NextSeed();

        /// <summary>
        /// The host changed this seat's hand without the seat's owner playing a card, for example by a deadline
        /// auto-play, a covering bot's move or a reclaim. The subscribe's private state covers the deal and every
        /// reconnect, and this call covers changes between them (<c>docs/match.md</c>, "Delivering a hand"). A
        /// environment with no client at that seat does nothing.
        /// </summary>
        void OnSeatHandChanged(int seat);

        /// <summary>
        /// The owner of a seat stopped playing it, for <paramref name="reason"/>. Called once per loss, and a seat
        /// that is reclaimed and lost again counts as two losses, so an environment can treat each call as a separate
        /// event. An environment with no recipient does nothing (<c>docs/match.md</c>, "Bot cover and seat reclaim").
        /// </summary>
        void OnSeatLost(int seat, MatchSeatLossReason reason);
    }

    /// <summary>
    /// The bot move a host is holding until its think delay ends. It is not part of the model because it is not
    /// game state. A table restored from the database makes a new decision instead.
    /// </summary>
    public sealed class MatchPendingBotMove
    {
        public bool     BotMovePending;
        public int      BotMovePlayIndex;
        public Card     BotMoveCard;
        public MetaTime BotMoveDueAt;

        /// <summary>Drop the held move, for example because the table has moved past its play index.</summary>
        public void Clear()
        {
            BotMovePending   = false;
            BotMovePlayIndex = -1;
            BotMoveDueAt     = MetaTime.Epoch;
        }

        /// <summary>
        /// Move the held move's play time forward to <paramref name="now"/>. Returns false when no move is held or
        /// its time has already come.
        /// </summary>
        public bool TryMakeDueNow(MetaTime now)
        {
            if (!BotMovePending || BotMoveDueAt <= now)
                return false;
            BotMoveDueAt = now;
            return true;
        }
    }

    /// <summary>
    /// The match turn flow, shared by the server's match actor and the browser's offline server so the game rules
    /// exist in one copy (<c>docs/web-client.md</c>, "Offline mode"). It turns events (a move, a connection change, a
    /// timer) into actions on the timeline. <see cref="IMatchHostEnvironment"/> supplies timers, sessions, persistence and
    /// message delivery, and <see cref="MatchSeatPolicy"/> holds the seat rules.
    /// <para>
    /// Every method reads <see cref="MatchModel.Engine"/>, so this class runs only on the host and never inside an
    /// action.
    /// </para>
    /// </summary>
    public static class MatchHost
    {
        /// <summary>
        /// The maximum number of state changes one <see cref="RunTable"/> call makes. A whole game needs far fewer,
        /// so reaching the limit means a loop, and the limit stops it.
        /// </summary>
        const int MaxStepsPerPass = 256;

        /// <summary>
        /// A deal seed from <see cref="RandomNumberGenerator"/>. A seed derived from the entity id, a tick count,
        /// a timestamp or a shared <c>Random</c> would let a client recompute the shuffle and see every hand,
        /// even though no secret is sent (<c>docs/match.md</c>, "The deal seed"). Every table with other players
        /// must use this seed. Only the browser's offline host uses a different one.
        /// </summary>
        public static ulong CreateDealSeed() =>
            BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)), 0);

        /// <summary>
        /// Deal a table and set up its seats. <paramref name="dealSeed"/> must come from <see cref="CreateDealSeed"/>.
        /// Play has not begun: the first move deadline starts only when every human seat has subscribed or the join
        /// window has ended, so a slow-loading client does not miss the first cards.
        /// </summary>
        /// <param name="tableSeed">
        /// Draws each bot seat's profile and cosmetics. It is separate from the deal seed so the draws do not depend on
        /// the cards.
        /// </param>
        /// <param name="botProfiles">The bot profiles from game config. The drawn profile is copied onto the table.</param>
        public static void SetupNewMatch(MatchModel model, ulong dealSeed, ulong tableSeed, List<MatchSeat> seats, MatchTimings timings, IReadOnlyList<BotProfile> botProfiles, MetaTime now)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (seats == null || seats.Count != MatchRules.NumSeats)
                throw new ArgumentException($"A table has exactly {MatchRules.NumSeats} seats", nameof(seats));

            MatchEngine engine = MatchEngine.Create(dealSeed, timings, now);

            // No seat has a deadline before play begins. MatchEngine.Create set the first deadline from the deal
            // time, so the join window would come out of the first player's turn. TryBeginPlay sets it later.
            engine.SetMoveDeadline(MetaTime.Epoch);

            // Bot seats get a profile and cosmetics drawn from the table seed and the current game config. A seat
            // with an owner gets neither: the caller already set its cosmetics from the player's own model.
            SharedGameConfig gameConfig = model.GameConfig as SharedGameConfig;

            List<BotProfile> profiles = new List<BotProfile>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                profiles.Add(seats[seat].IsPlayedByBot ? BotProfiles.DrawForSeat(botProfiles, tableSeed, seat) : null);

                if (!seats[seat].HasOwner)
                    seats[seat].ReplaceIdentity(BotSeatIdentity.Identity(gameConfig, tableSeed, seat, seats[seat].DisplayName));
            }

            model.Setup(engine, seats, profiles, now);
        }

        #region Moves

        /// <summary>
        /// Handle a move from a client. The seat index in the message is untrusted, so it is checked against the
        /// submitter's seat before <see cref="MatchEngine.PlanMove"/>, which throws on an out-of-range seat.
        /// Any move from the seat's owner, even a refused one, marks the owner present: the lapse count is reset and a
        /// covering bot returns the seat before the move is planned. The covering bot often plays the same play index
        /// first, so a reclaim that required an accepted move would rarely succeed (<c>docs/match.md</c>, "The play
        /// index"). The action is published before the engine commits the move, as <see cref="MovePlan"/> explains.
        /// </summary>
        /// <returns><see cref="MoveRefusalReason.None"/> when the move was published and played; a reason otherwise.</returns>
        public static MoveRefusalReason TrySubmitMove(MatchModel model, EntityId submitterId, int seat, int playIndex, Card card, MetaTime now, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);

            if (model.Phase != MatchPhase.Playing)
                return MoveRefusalReason.NotInPlayablePhase;
            if (seat < 0 || seat >= MatchRules.NumSeats)
                return MoveRefusalReason.NotYourSeat;

            // A bot seat's PlayerId is EntityId.None, so an invalid submitter id would match every bot seat. The
            // host plays bot seats through TryPlayForSeat, which takes no submitter.
            if (!submitterId.IsValid)
                return MoveRefusalReason.NotYourSeat;
            if (model.GetSeat(seat).PlayerId != submitterId)
                return MoveRefusalReason.NotYourSeat;

            NoteSeatPresent(model, seat, now, environment);

            MoveResult result = model.Engine.PlanMove(seat, playIndex, card, now, MoveTimingsFor(model), out MovePlan plan);
            if (!result.Accepted)
                return result.Refusal;

            if (!PublishAndCommit(model, plan, now, environment))
                return MoveRefusalReason.NotPublished;

            return MoveRefusalReason.None;
        }

        /// <summary>
        /// Play a card for a seat that the host plays: a bot seat, a deadline auto-play or a play-out. The caller
        /// has already decided that the host plays this turn, so there is no identity check.
        /// </summary>
        /// <returns>
        /// Whether the card was published and played. False when the engine refused the move or publishing failed.
        /// </returns>
        public static bool TryPlayForSeat(MatchModel model, int seat, Card card, MetaTime now, IMatchHostEnvironment environment)
            => TryPlayForSeat(model, seat, card, now, MoveTimingsFor(model), environment);

        /// <summary>
        /// Play a card for a seat with the given <paramref name="timings"/> instead of the seat-based ones. A
        /// play-out passes <see cref="MoveTimings.Instant"/>.
        /// </summary>
        public static bool TryPlayForSeat(MatchModel model, int seat, Card card, MetaTime now, MoveTimings timings, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);

            MoveResult result = model.Engine.PlanMove(seat, model.Engine.PlayIndex, card, now, timings, out MovePlan plan);
            if (!result.Accepted)
                return false;

            return PublishAndCommit(model, plan, now, environment);
        }

        /// <summary>
        /// End the resolve pause if it has ended by <paramref name="now"/>, starting the next trick or finishing
        /// the game. Does nothing otherwise, so a host can call it on every update. Like
        /// <see cref="TrySubmitMove"/>, it publishes the action before the engine commits.
        /// </summary>
        public static bool TryAdvance(MatchModel model, MetaTime now, IMatchHostEnvironment environment)
            => TryAdvance(model, now, DeadlineAfterPause(model), environment);

        /// <summary>
        /// Like <see cref="TryAdvance(MatchModel, MetaTime, IMatchHostEnvironment)"/>, but gives the next trick's leader
        /// <paramref name="nextMoveDeadline"/>.
        /// </summary>
        public static bool TryAdvance(MatchModel model, MetaTime now, MetaDuration nextMoveDeadline, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);

            if (model.Phase != MatchPhase.Playing)
                return false;
            if (!model.Engine.PlanAdvance(now, nextMoveDeadline, out AdvancePlan plan))
                return false;

            MatchPhase matchPhase = plan.TurnPhase == MatchTurnPhase.Finished ? MatchPhase.Ended : MatchPhase.Playing;
            if (!environment.Publish(new MatchAdvanced(plan.TurnPhase, matchPhase, plan.MoveDeadlineAt, matchPhase == MatchPhase.Playing ? MetaTime.Epoch : now)))
                return false;

            model.Engine.CommitAdvance(plan);
            return true;
        }

        #endregion

        #region Seats coming and going

        /// <summary>
        /// The seat's owner is present because they subscribed, reconnected or sent a move. Their grace timer
        /// stops, their lapse count resets, and a covering bot returns the seat. The same action updates the
        /// move deadline of the seat on turn, so a connected human seat on turn always has a deadline.
        /// </summary>
        /// <returns>Whether anything changed.</returns>
        public static bool NoteSeatPresent(MatchModel model, int seat, MetaTime now, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);
            if (model.Phase != MatchPhase.Playing)
                return false;
            if (seat < 0 || seat >= MatchRules.NumSeats || !model.GetSeat(seat).HasOwner)
                return false;

            List<MatchSeatState> states = MatchSeatPolicy.CurrentStates(model);
            states[seat] = MatchSeatPolicy.Present(states[seat]);

            bool reclaimed = model.GetSeat(seat).Occupancy == MatchSeatOccupancy.HumanCoveredByBot;
            if (!PublishSeats(model, states, now, environment))
                return false;

            // The owner is back, so clear the loss reason. Otherwise CaptureResults would report the old reason as
            // if the player had never returned.
            model.NoteSeatLossReason(seat, MatchSeatLossReason.None);

            // A covering bot played cards from this hand. The board shows which, but every hand change the owner
            // did not make gets a hand delivery (docs/match.md, "Delivering a hand").
            if (reclaimed)
                environment.OnSeatHandChanged(seat);

            return true;
        }

        /// <summary>
        /// The seat's owner has gone because their session ended or they used the Leave control. Leave passes
        /// <paramref name="skipGrace"/> and a bot covers the seat immediately. A dropped connection gets a grace
        /// timer, so a tab switch or a network interruption does not cost the seat. In both cases the game is
        /// played to the end and recorded.
        /// </summary>
        public static bool NoteSeatAbsent(MatchModel model, int seat, MetaTime now, bool skipGrace, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);
            if (model.Phase != MatchPhase.Playing)
                return false;
            if (seat < 0 || seat >= MatchRules.NumSeats || !model.GetSeat(seat).HasOwner)
                return false;

            List<MatchSeatState> states = MatchSeatPolicy.CurrentStates(model);
            states[seat] = MatchSeatPolicy.Absent(states[seat], now, model.Engine.Timings.DisconnectGrace, skipGrace);
            if (!PublishSeats(model, states, now, environment))
                return false;

            // Only the Leave control skips grace, so skipGrace is the only way to tell a deliberate leave from a
            // disconnect (docs/match.md, "Leaving the table").
            NoteSeatLost(model, seat, skipGrace ? MatchSeatLossReason.DeliberateLeave : MatchSeatLossReason.Disconnect, environment);
            return true;
        }

        /// <summary>
        /// Record why the owner stopped playing a seat, and notify the environment only when
        /// <see cref="MatchModel.NoteSeatLossReason"/> recorded a new loss.
        /// </summary>
        static void NoteSeatLost(MatchModel model, int seat, MatchSeatLossReason reason, IMatchHostEnvironment environment)
        {
            if (model.NoteSeatLossReason(seat, reason))
                environment.OnSeatLost(seat, reason);
        }

        /// <summary>
        /// The table was restored from the database. No client is subscribed to a just-restored actor, so every
        /// connected flag is cleared, and every owned seat that was connected gets the longer restart grace timer
        /// because all clients reconnect at once (see <see cref="MatchSeatPolicy.StatesAfterColdWake"/>).
        /// <para>
        /// Without the grace timers, the table would look deserted on its first update and would be played out
        /// immediately. The play-out rule acts only after a grace timer has ended
        /// (<c>docs/match.md</c>, "Restart and cold wake").
        /// </para>
        /// </summary>
        public static bool NoteColdWake(MatchModel model, MetaTime now, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);
            if (model.Phase != MatchPhase.Playing)
                return false;

            List<MatchSeatState> states = MatchSeatPolicy.StatesAfterColdWake(model, now, model.Engine.Timings.RestartReconnectGrace);
            return PublishSeats(model, states, now, environment);
        }

        #endregion

        #region Driving the table

        /// <summary>
        /// Advance the table as far as possible at <paramref name="now"/>. Both hosts call this: the server actor
        /// from its timers and message handlers, and the browser's offline server every frame.
        /// <para>
        /// The steps run in this order: the join window, because nobody plays before it ends; the grace timers,
        /// because an ended grace changes who plays a seat; the play-out check, because a table with no human
        /// left waits for nothing; then the game itself.
        /// </para>
        /// </summary>
        public static void RunTable(MatchModel model, MatchPendingBotMove pendingBotMove, MetaTime now, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);
            if (pendingBotMove == null)
                throw new ArgumentNullException(nameof(pendingBotMove));

            RunTableSteps(model, pendingBotMove, now, environment);

            // The match can end on several paths through RunTableSteps, such as the last trick resolving or a
            // play-out. Capturing after every call records the results on the call where the match ended.
            // CaptureResults does nothing on other calls.
            CaptureResults(model);
        }

        /// <summary>
        /// The turn flow. It is separate from <see cref="RunTable"/> so that every return path is followed by
        /// <see cref="CaptureResults"/>.
        /// </summary>
        static void RunTableSteps(MatchModel model, MatchPendingBotMove pendingBotMove, MetaTime now, IMatchHostEnvironment environment)
        {
            for (int step = 0; step < MaxStepsPerPass; step++)
            {
                if (model.Phase != MatchPhase.Playing)
                    return;

                if (!model.PlayHasBegun)
                {
                    if (!MatchSeatPolicy.ShouldBeginPlay(model, now))
                        return;
                    if (!TryBeginPlay(model, now, environment))
                        return;
                    continue;
                }

                if (TryLapseGrace(model, now, environment))
                    continue;

                if (MatchSeatPolicy.NobodyIsComingBack(model, now))
                {
                    PlayOut(model, pendingBotMove, now, environment);
                    return;
                }

                if (TrySyncDeadline(model, now, environment))
                    continue;

                if (model.Board.TurnPhase == MatchTurnPhase.ResolvingTrick)
                {
                    // The pause has not ended, or the advance could not be published. The table waits.
                    if (!TryAdvance(model, now, environment))
                        return;
                    continue;
                }

                if (model.Board.TurnPhase != MatchTurnPhase.AwaitingMove)
                    return;

                int seat = model.Board.SeatOnTurn;
                if (!model.GetSeat(seat).IsPlayedByBot)
                {
                    if (!MatchSeatPolicy.MoveDeadlineHasLapsed(model, now))
                    {
                        // A human seat is on turn and its deadline has not passed, or it has no deadline (a
                        // disconnected seat in grace, or a table without deadlines). A held bot move is for an
                        // earlier play index, so drop it.
                        pendingBotMove.Clear();
                        return;
                    }

                    if (!TryAutoPlayLapsedSeat(model, seat, now, environment))
                        return;
                    continue;
                }

                if (!TryPlayBotSeat(model, pendingBotMove, seat, now, environment))
                    return;
            }
        }

        /// <summary>
        /// Start play when the join window ends. If no human ever subscribed, the table is abandoned instead.
        /// Otherwise a bot covers every human seat that never subscribed, with no grace timer.
        /// </summary>
        static bool TryBeginPlay(MatchModel model, MetaTime now, IMatchHostEnvironment environment)
        {
            if (MatchSeatPolicy.IsAbandonedAtStart(model))
                return environment.Publish(new MatchAbandoned(now));

            List<MatchSeatState> states = MatchSeatPolicy.CurrentStates(model);
            for (int seat = 0; seat < states.Count; seat++)
            {
                if (model.GetSeat(seat).HasOwner && !states[seat].HasEverConnected)
                    states[seat] = MatchSeatPolicy.Cover(states[seat]);
            }

            MetaTime deadlineAt = DeadlineStampAfter(model, states, now, playHasBegun: true);
            return PublishSeatsWithDeadline(model, states, deadlineAt, playHasBegun: true, environment);
        }

        /// <summary>Cover every seat whose grace has run out. Returns whether anything changed.</summary>
        static bool TryLapseGrace(MatchModel model, MetaTime now, IMatchHostEnvironment environment)
        {
            List<MatchSeatState> states  = null;
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                if (!MatchSeatPolicy.GraceHasLapsed(model.GetSeat(seat), now))
                    continue;

                states ??= MatchSeatPolicy.CurrentStates(model);
                states[seat] = MatchSeatPolicy.Cover(states[seat]);
            }

            if (states == null)
                return false;

            return PublishSeats(model, states, now, environment);
        }

        /// <summary>
        /// Update the board's move deadline when it no longer matches what the seat on turn should have, for
        /// example after that seat reconnected or disconnected during its turn.
        /// </summary>
        static bool TrySyncDeadline(MatchModel model, MetaTime now, IMatchHostEnvironment environment)
        {
            if (!MatchSeatPolicy.NeedsDeadlineChange(model, model.Engine.Timings, now, out MetaTime deadlineAt))
                return false;

            return PublishSeatsWithDeadline(model, MatchSeatPolicy.CurrentStates(model), deadlineAt, playHasBegun: false, environment);
        }

        /// <summary>
        /// The seat on turn passed its deadline. The lapse is counted and <see cref="BotPolicy.DecideAutoPlay"/>
        /// plays its card. When <see cref="MatchSeatPolicy.ShouldCoverAfterLapses"/> says so, a bot covers the seat,
        /// and the owner can reclaim it at any time before the game ends.
        /// </summary>
        static bool TryAutoPlayLapsedSeat(MatchModel model, int seat, MetaTime now, IMatchHostEnvironment environment)
        {
            List<MatchSeatState> states = MatchSeatPolicy.CurrentStates(model);
            MatchSeatState       state  = states[seat];

            state.ConsecutiveMissedDeadlines++;
            bool covered = MatchSeatPolicy.ShouldCoverAfterLapses(state.ConsecutiveMissedDeadlines, model.Engine.Timings);
            if (covered)
                state = MatchSeatPolicy.Cover(state);
            states[seat] = state;

            // Publish the lapse and clear the deadline before playing the card, so the seat never keeps a
            // deadline that has already passed.
            if (!PublishSeatsWithDeadline(model, states, MetaTime.Epoch, playHasBegun: false, environment))
                return false;

            // A connected player who stopped playing loses the seat for DeadlineLapse. The connection state
            // does not show this case.
            if (covered)
                NoteSeatLost(model, seat, MatchSeatLossReason.DeadlineLapse, environment);

            BotDecision decision = BotPolicy.DecideAutoPlay(MatchSeatView.ForSeat(model.Engine, seat), environment.NextSeed());
            if (!TryPlayForSeat(model, seat, decision.Card, now, environment))
                return false;

            // The owner did not play this card, so their client needs a hand delivery.
            environment.OnSeatHandChanged(seat);
            return true;
        }

        /// <summary>
        /// Play the turn of a bot seat, either a bot from formation or a bot covering an absent human. The first
        /// call decides the move and holds it in <paramref name="pendingBotMove"/> until its think delay ends. A later
        /// call plays it. <see cref="BotPolicy.DecideCover"/> gives a covered seat with a connected owner a long delay
        /// so the owner can reclaim it by playing a card.
        /// </summary>
        static bool TryPlayBotSeat(MatchModel model, MatchPendingBotMove pendingBotMove, int seat, MetaTime now, IMatchHostEnvironment environment)
        {
            if (!pendingBotMove.BotMovePending || pendingBotMove.BotMovePlayIndex != model.Board.PlayIndex)
            {
                BotDecision decision = DecideBotMove(model, seat, environment.NextSeed());
                pendingBotMove.BotMovePending   = true;
                pendingBotMove.BotMovePlayIndex = decision.PlayIndex;
                pendingBotMove.BotMoveCard      = decision.Card;
                pendingBotMove.BotMoveDueAt     = now + decision.ThinkDelay;
            }

            if (now < pendingBotMove.BotMoveDueAt)
                return false;

            pendingBotMove.BotMovePending = false;

            bool hasOwner = model.GetSeat(seat).HasOwner;
            if (!TryPlayForSeat(model, seat, pendingBotMove.BotMoveCard, now, environment))
                return false;

            if (hasOwner)
                environment.OnSeatHandChanged(seat);
            return true;
        }

        /// <summary>
        /// Finish a table with no human left. Bots play every seat with no think delays and no resolve pauses, the
        /// remaining tricks resolve in one call, and the match ends with standings.
        /// <para>
        /// The table is not abandoned instead, because most tables have one human. If closing the tab erased the
        /// game, quitting would avoid every loss. Playing out records a result for the player who left
        /// (<c>docs/match.md</c>, "Play-out").
        /// </para>
        /// </summary>
        static void PlayOut(MatchModel model, MatchPendingBotMove pendingBotMove, MetaTime now, IMatchHostEnvironment environment)
        {
            pendingBotMove.Clear();

            for (int step = 0; step < MaxStepsPerPass; step++)
            {
                if (model.Phase != MatchPhase.Playing)
                    return;

                if (model.Board.TurnPhase == MatchTurnPhase.ResolvingTrick)
                {
                    // Do not wait for a resolve pause that started before the last human left. Nobody is watching.
                    MetaTime pauseEndsAt = model.Board.ResolvePauseEndsAt;
                    if (!TryAdvance(model, pauseEndsAt > now ? pauseEndsAt : now, MetaDuration.Zero, environment))
                        return;
                    continue;
                }

                if (model.Board.TurnPhase != MatchTurnPhase.AwaitingMove)
                    return;

                int         seat     = model.Board.SeatOnTurn;
                BotDecision decision = BotPolicy.DecidePlayOut(MatchSeatView.ForSeat(model.Engine, seat), environment.NextSeed());
                if (!TryPlayForSeat(model, seat, decision.Card, now, MoveTimings.Instant, environment))
                    return;
            }
        }

        /// <summary>
        /// Record each human seat's result once, when the match reaches <see cref="MatchPhase.Ended"/>. An
        /// <see cref="MatchPhase.Abandoned"/> match never started and records no result
        /// (<c>docs/match.md</c>, "Phases").
        /// <para>
        /// This runs here and not in the action that ends the match because it writes a server-only member, and
        /// actions also run on clients, where server-only members do not exist
        /// (<c>docs/match.md</c>, "Action payload rules").
        /// </para>
        /// </summary>
        static void CaptureResults(MatchModel model)
        {
            if (model.Phase != MatchPhase.Ended || model.HasCapturedResults)
                return;

            // Position and tricks come from the standings that MatchAdvanced stored, so the recorded result matches
            // the results screen.
            List<MatchSeatResult> results = new List<MatchSeatResult>(MatchRules.NumSeats);
            foreach (SeatStanding standing in model.Board.Standings)
            {
                MatchSeat seat = model.GetSeat(standing.Seat);
                if (!seat.HasOwner)
                    continue;

                // A seat whose owner never subscribed was played by a bot for the whole game, so it does not
                // count as a human opponent.
                int humanOpponents = 0;
                foreach (MatchSeat other in model.Seats)
                {
                    if (other.Seat != seat.Seat && other.HasOwner && other.HasEverConnected)
                        humanOpponents++;
                }

                // The owner finished the game only if the seat is not covered by a bot and is connected. This is
                // captured now because NoteColdWake clears every connected flag after a restore.
                bool finishedByPlayer = seat.Occupancy == MatchSeatOccupancy.Human && seat.IsConnected;

                results.Add(new MatchSeatResult(
                    seat.Seat, seat.PlayerId, standing.Position, standing.TricksWon,
                    humanOpponents, finishedByPlayer, model.GetSeatLossReason(seat.Seat)));
            }

            model.CaptureSeatResults(results);
        }

        #endregion

        #region What the host waits on

        /// <summary>
        /// The earliest time the host must call <see cref="RunTable"/> again, or <see cref="MetaTime.Epoch"/> if no
        /// timer is pending. The match entity does not tick, so the host schedules each wake from the absolute
        /// times in the model, also after restoring a table (<c>docs/match.md</c>, "Timers").
        /// <para>
        /// The held bot move in <paramref name="pendingBotMove"/> is included. It is not in the model, and without it a
        /// table waiting only for a bot move would never wake.
        /// </para>
        /// </summary>
        public static MetaTime GetNextWakeAt(MatchModel model, MatchPendingBotMove pendingBotMove)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (model.Phase != MatchPhase.Playing)
                return MetaTime.Epoch;

            MetaTime wakeAt = MetaTime.Epoch;

            if (!model.PlayHasBegun)
                wakeAt = Earlier(wakeAt, model.JoinWindowEndsAt);

            foreach (MatchSeat seat in model.Seats)
            {
                if (seat.IsInGrace)
                    wakeAt = Earlier(wakeAt, seat.GraceEndsAt);
            }

            MatchBoard board = model.Board;
            if (board.TurnPhase == MatchTurnPhase.ResolvingTrick)
                wakeAt = Earlier(wakeAt, board.ResolvePauseEndsAt);
            else if (board.TurnPhase == MatchTurnPhase.AwaitingMove && board.HasMoveDeadline)
                wakeAt = Earlier(wakeAt, board.MoveDeadlineAt);

            if (pendingBotMove != null && pendingBotMove.BotMovePending)
                wakeAt = Earlier(wakeAt, pendingBotMove.BotMoveDueAt);

            return wakeAt;
        }

        /// <summary>
        /// Move every pending timer of this table forward to <paramref name="now"/>, so tests can trigger timers that
        /// are configured never to fire during a test (<c>docs/testing.md</c>, "Forcing timers"). It covers every
        /// timer that <see cref="GetNextWakeAt"/> reads, and changes only the timer values. The caller then runs the
        /// table, which applies the normal lapse, cover and play-out rules. No action field can move the resolve pause
        /// end, so the pause is ended by running the advance at the pause's end time, as the wake would.
        /// </summary>
        /// <returns>How many timers were moved forward.</returns>
        public static int ForceExpireTimers(MatchModel model, MatchPendingBotMove pendingBotMove, MetaTime now, IMatchHostEnvironment environment)
        {
            CheckArgs(model, environment);
            if (model.Phase != MatchPhase.Playing)
                return 0;

            int numTimersExpired = 0;

            List<MatchSeatState> states           = MatchSeatPolicy.CurrentStates(model);
            bool                 changed          = false;
            MetaTime             joinWindowEndsAt = MetaTime.Epoch;

            if (!model.PlayHasBegun && model.JoinWindowEndsAt > now)
            {
                joinWindowEndsAt = now;
                changed          = true;
                numTimersExpired++;
            }

            for (int seat = 0; seat < states.Count; seat++)
            {
                MatchSeatState state = states[seat];
                if (!state.IsInGrace || state.GraceEndsAt <= now)
                    continue;

                state.GraceEndsAt = now;
                states[seat]      = state;
                changed           = true;
                numTimersExpired++;
            }

            MetaTime deadlineAt = model.Board.MoveDeadlineAt;
            if (model.Board.TurnPhase == MatchTurnPhase.AwaitingMove && model.Board.HasMoveDeadline && deadlineAt > now)
            {
                deadlineAt = now;
                changed    = true;
                numTimersExpired++;
            }

            if (changed && !PublishSeatsWithDeadline(model, states, deadlineAt, playHasBegun: false, environment, joinWindowEndsAt))
                return 0;

            MetaTime pauseEndsAt = model.Board.ResolvePauseEndsAt;
            if (model.Board.TurnPhase == MatchTurnPhase.ResolvingTrick && pauseEndsAt > now && TryAdvance(model, pauseEndsAt, environment))
                numTimersExpired++;

            if (pendingBotMove != null && pendingBotMove.TryMakeDueNow(now))
                numTimersExpired++;

            return numTimersExpired;
        }

        #endregion

        #region Bots and hands

        /// <summary>
        /// The move and think delay for the bot at <paramref name="seat"/>. The policy receives a
        /// <see cref="MatchSeatView"/>, which does not contain other seats' cards.
        /// </summary>
        public static BotDecision DecideBotMove(MatchModel model, int seat, ulong seed)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            MatchSeatView view = MatchSeatView.ForSeat(model.Engine, seat);

            // A covered seat uses DecideCover, which always plays the strongest profile, so a bot never makes a
            // deliberate mistake with a human's cards.
            if (model.GetSeat(seat).Occupancy == MatchSeatOccupancy.HumanCoveredByBot)
                return BotPolicy.DecideCover(view, model.Engine.Timings, seed, model.GetSeat(seat).IsConnected);

            // A bot seat with no stored profile plays with the strongest profile rather than not at all.
            BotProfile profile = model.GetSeatBotProfile(seat) ?? BotProfiles.Strongest;

            return BotPolicy.Decide(view, profile, model.Engine.Timings, seed);
        }

        /// <summary>The hand delivery for one seat, with the play index at which the hand was read.</summary>
        public static MatchHandDelivered BuildHandDelivery(MatchModel model, int seat)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            return new MatchHandDelivered(seat, model.Engine.GetHand(seat), model.Board.PlayIndex);
        }

        #endregion

        #region Publishing

        static bool PublishAndCommit(MatchModel model, MovePlan plan, MetaTime now, IMatchHostEnvironment environment)
        {
            MatchCardPlayed action = new MatchCardPlayed(
                seat:               plan.Seat,
                card:               plan.Card,
                turnPhase:          plan.TurnPhase,
                moveDeadlineAt:     plan.MoveDeadlineAt,
                resolvePauseEndsAt: plan.ResolvePauseEndsAt,
                trickWinnerSeat:    plan.TrickWinnerSeat,
                playedAt:           now);

            if (!environment.Publish(action))
                return false;

            model.Engine.CommitMove(plan);
            return true;
        }

        /// <summary>
        /// Publish a seat update that does not start play, with the move deadline that <paramref name="states"/>
        /// give the seat on turn.
        /// </summary>
        static bool PublishSeats(MatchModel model, List<MatchSeatState> states, MetaTime now, IMatchHostEnvironment environment)
            => PublishSeatsWithDeadline(model, states, DeadlineStampAfter(model, states, now, model.PlayHasBegun), playHasBegun: false, environment);

        /// <summary>
        /// Dry-runs <paramref name="action"/> on <paramref name="model"/> and logs a refusal. A host calls this
        /// before it puts an action on the timeline: the host commits server-only engine state, which is not
        /// checksummed, only after a publish succeeds, so it must learn of a refusal before the action runs.
        /// </summary>
        public static bool PassesDryRun(MatchModel model, MatchAction action, IMetaLogger log)
        {
            MetaActionResult dryRun = action.InvokeExecute(model, commit: false);
            if (dryRun.IsSuccess)
                return true;

            log.Error("Refusing to publish {Action}: {Result}. The game state is left untouched.", action.GetType().Name, dryRun);
            return false;
        }

        /// <summary>
        /// Publish a seat update and then set the engine's copy of the move deadline, in that order, like every
        /// other publish-then-commit in this class. The engine and the board each store the deadline, so both
        /// must be updated to keep them equal.
        /// </summary>
        static bool PublishSeatsWithDeadline(MatchModel model, List<MatchSeatState> states, MetaTime deadlineAt, bool playHasBegun, IMatchHostEnvironment environment, MetaTime joinWindowEndsAt = default)
        {
            // Skip an update that changes nothing. TrySubmitMove notes presence on every move, and usually nothing
            // changes, so without this check every tap would publish an action.
            if (ChangesNothing(model, states, deadlineAt, playHasBegun, joinWindowEndsAt))
                return true;

            if (!environment.Publish(new MatchSeatsUpdated(states, deadlineAt, playHasBegun, joinWindowEndsAt)))
                return false;

            model.Engine.SetMoveDeadline(deadlineAt);
            return true;
        }

        static bool ChangesNothing(MatchModel model, List<MatchSeatState> states, MetaTime deadlineAt, bool playHasBegun, MetaTime joinWindowEndsAt)
        {
            if (playHasBegun && !model.PlayHasBegun)
                return false;
            if (joinWindowEndsAt > MetaTime.Epoch && joinWindowEndsAt != model.JoinWindowEndsAt)
                return false;
            if (deadlineAt != model.Board.MoveDeadlineAt)
                return false;

            for (int seat = 0; seat < states.Count; seat++)
            {
                if (!MatchSeatState.Of(model.GetSeat(seat)).Equals(states[seat]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The move deadline for the seat on turn under <paramref name="states"/>. A connected human seat keeps its
        /// current deadline, or gets a new one if it has none. Any other seat gets no deadline, and no seat gets one
        /// before play begins, so the join window is never taken out of the first turn.
        /// </summary>
        static MetaTime DeadlineStampAfter(MatchModel model, List<MatchSeatState> states, MetaTime now, bool playHasBegun)
        {
            if (!playHasBegun)
                return MetaTime.Epoch;

            MatchBoard board = model.Board;
            if (board.TurnPhase != MatchTurnPhase.AwaitingMove)
                return MetaTime.Epoch;

            int seat = board.SeatOnTurn;
            if (seat < 0)
                return MetaTime.Epoch;

            MetaDuration owed = MatchSeatPolicy.MoveDeadlineFor(states[seat], model.Engine.Timings);
            if (owed <= MetaDuration.Zero)
                return MetaTime.Epoch;

            // Keep a running deadline. Restarting it whenever another seat's connection changed could extend the
            // turn without limit.
            return board.HasMoveDeadline ? board.MoveDeadlineAt : now + owed;
        }

        /// <summary>
        /// The move deadline duration for the seat that leads after the resolve pause, from
        /// <see cref="MatchSeatPolicy.MoveDeadlineFor"/>, because only a connected human seat gets a deadline.
        /// </summary>
        static MetaDuration DeadlineAfterPause(MatchModel model)
        {
            int seat = model.Engine.SeatOnTurnAfterPause;
            return seat < 0 ? MetaDuration.Zero : MatchSeatPolicy.MoveDeadlineFor(model.GetSeat(seat), model.Engine.Timings);
        }

        static MoveTimings MoveTimingsFor(MatchModel model)
        {
            MatchTimings timings = model.Engine.Timings;

            // The deadline is for the seat on turn after this card. SeatOnTurnAfterNextPlay is -1 when this card
            // completes a trick. The next leader's deadline is then set by DeadlineAfterPause when the pause ends.
            // A card played before play begins arms nothing: TryBeginPlay gives the seat on turn its deadline then.
            int          nextSeat = model.Engine.SeatOnTurnAfterNextPlay;
            MetaDuration deadline = nextSeat < 0 || !model.PlayHasBegun ? MetaDuration.Zero : MatchSeatPolicy.MoveDeadlineFor(model.GetSeat(nextSeat), timings);
            return new MoveTimings(deadline, timings.ResolvePause);
        }

        /// <summary>
        /// The earlier of two times, where <see cref="MetaTime.Epoch"/> (or earlier) means "not set": an unset
        /// <paramref name="candidate"/> keeps <paramref name="current"/>, and an unset <paramref name="current"/>
        /// takes <paramref name="candidate"/>. <see cref="MetaTime.Min"/> would pick the unset time.
        /// </summary>
        public static MetaTime Earlier(MetaTime current, MetaTime candidate)
        {
            if (candidate <= MetaTime.Epoch)
                return current;
            if (current == MetaTime.Epoch || candidate < current)
                return candidate;
            return current;
        }

        static void CheckArgs(MatchModel model, IMatchHostEnvironment environment)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
        }

        #endregion
    }
}
