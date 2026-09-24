using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.Matchmaking
{
    public sealed partial class MatchmakerActor
    {
        /// <summary>
        /// What one formation attempt came back with. Assembled off the actor context and applied on it, so
        /// the queue is only ever touched by the actor itself.
        /// </summary>
        readonly struct FormationResult
        {
            public readonly MatchmakingSeatAnswer AnswerA;
            public readonly MatchmakingSeatAnswer AnswerB;
            public readonly bool                  MintOk;
            public readonly EntityId              MatchId;

            public FormationResult(MatchmakingSeatAnswer answerA, MatchmakingSeatAnswer answerB, bool mintOk, EntityId matchId)
            {
                AnswerA = answerA;
                AnswerB = answerB;
                MintOk  = mintOk;
                MatchId = matchId;
            }
        }

        // ---------------------------------------------------------------- two humans

        /// <summary>
        /// Form a table for two waiters, off the actor's own context: two asks and a mint must not block the
        /// singleton every other player passes through. The tickets left the queue before this started.
        /// </summary>
        void StartPairFormation(MatchmakingTicket a, MatchmakingTicket b)
        {
            SharedGameConfig config = SharedConfig;

            ContinueTaskOnActorContext(
                FormPairAsync(a, b, config),
                result => ApplyPairOutcome(a, b, result),
                error =>
                {
                    // The task itself failed. Both seats go back with their stamps, as for a failed mint.
                    _log.Warning("Forming a table for {A} and {B} failed ({Error}); both go back in the queue", a.PlayerId, b.PlayerId, error.GetType().Name);
                    ApplyPairOutcome(a, b, new FormationResult(MatchmakingSeatAnswer.Accepted, MatchmakingSeatAnswer.Accepted, mintOk: false, EntityId.None));
                });
        }

        async Task<FormationResult> FormPairAsync(MatchmakingTicket a, MatchmakingTicket b, SharedGameConfig config)
        {
            // Together, so the formation costs the longer of the two asks rather than their sum.
            Task<MatchmakingSeatAnswer> askA = AskForSeatAsync(a);
            Task<MatchmakingSeatAnswer> askB = AskForSeatAsync(b);
            await Task.WhenAll(askA, askB);

            MatchmakingSeatAnswer answerA = await askA;
            MatchmakingSeatAnswer answerB = await askB;

            if (answerA != MatchmakingSeatAnswer.Accepted || answerB != MatchmakingSeatAnswer.Accepted)
                return new FormationResult(answerA, answerB, mintOk: false, EntityId.None);

            MatchSetupParams setup  = BuildPairSetup(a, b, config);
            EntityId         minted = await MintAsync(setup);

            return new FormationResult(answerA, answerB, mintOk: minted.IsValid, minted);
        }

        void ApplyPairOutcome(MatchmakingTicket a, MatchmakingTicket b, FormationResult result)
        {
            FormationOutcome outcome = FormationOutcomePolicy.Decide(result.AnswerA, result.AnswerB, result.MintOk);

            if (outcome.Formed)
                _log.Info("Table {MatchId} seats {A} against {B}", result.MatchId, a.PlayerId, b.PlayerId);
            else
                _log.Info("The pairing of {A} ({AnswerA}) and {B} ({AnswerB}) dissolved", a.PlayerId, result.AnswerA, b.PlayerId, result.AnswerB);

            ApplySeatAction(a, outcome.SeatA, result.MatchId, seat: 0);
            ApplySeatAction(b, outcome.SeatB, result.MatchId, seat: 1);

            // Immediately: a released ticket already past the fill wait takes the bot fallback in this pass.
            RearmFromQueue();
        }

        // ---------------------------------------------------------------- one human and a bot

        /// <summary>
        /// The fill wait ran out, so the queue forms a real match against a labelled bot at practice stakes.
        /// </summary>
        void StartBotFormation(MatchmakingTicket waiter)
        {
            SharedGameConfig config = SharedConfig;

            ContinueTaskOnActorContext(
                FormBotMatchAsync(waiter, config),
                result => ApplyBotOutcome(waiter, result),
                // Requeued, because a mint failure is usually transient. A deterministic one (an archive with no
                // starter decks) requeues the waiter without bound; bounding it needs a per-ticket failure count.
                error =>
                {
                    _log.Warning("Forming a bot table for {Player} failed ({Error}); it goes back in the queue", waiter.PlayerId, error.GetType().Name);
                    ApplyBotOutcome(waiter, new FormationResult(MatchmakingSeatAnswer.Accepted, MatchmakingSeatAnswer.Accepted, mintOk: false, EntityId.None));
                });
        }

        async Task<FormationResult> FormBotMatchAsync(MatchmakingTicket waiter, SharedGameConfig config)
        {
            // The account still commits to the seat: a player who cancelled or left must not be handed one.
            MatchmakingSeatAnswer answer = await AskForSeatAsync(waiter);
            if (answer != MatchmakingSeatAnswer.Accepted)
                return new FormationResult(answer, MatchmakingSeatAnswer.Accepted, mintOk: false, EntityId.None);

            MatchSetupParams setup  = BuildBotSetup(waiter, config);
            EntityId         minted = await MintAsync(setup);

            return new FormationResult(answer, MatchmakingSeatAnswer.Accepted, mintOk: minted.IsValid, minted);
        }

        void ApplyBotOutcome(MatchmakingTicket waiter, FormationResult result)
        {
            bool formed = result.AnswerA == MatchmakingSeatAnswer.Accepted && result.MintOk;

            if (formed)
                _log.Info("Table {MatchId} seats {Player} against the fill-wait bot", result.MatchId, waiter.PlayerId);

            ApplySeatAction(waiter, FormationOutcomePolicy.ForSeat(result.AnswerA, formed), result.MatchId, seat: 0);
            RearmFromQueue();
        }

        // ---------------------------------------------------------------- the pieces

        Task<EntityId> MintAsync(MatchSetupParams setup)
            => MatchMinting.MintAsync(
                matchId => EntityAskAsync(matchId, new InternalEntitySetupRequest(setup), Options.MintTimeout),
                matchId => EntityAskAsync(matchId, new InternalMatchAbandonRequest(), TimeSpan.FromSeconds(2)),
                _log);

        /// <summary>
        /// Ask one account to commit to a seat. Anything other than an explicit answer is a lost answer: a
        /// refusal proves the actor is responsive, and a failure proves nothing.
        /// </summary>
        async Task<MatchmakingSeatAnswer> AskForSeatAsync(MatchmakingTicket ticket)
        {
            try
            {
                InternalPlayerSeatInMatchResponse response = await EntityAskAsync(
                    ticket.PlayerId, new InternalPlayerSeatInMatchRequest(), Options.SeatAskTimeout);

                if (response.Accepted)
                    return MatchmakingSeatAnswer.Accepted;

                _log.Debug("{PlayerId} declined a seat: {Reason}", ticket.PlayerId, response.Reason);
                return MatchmakingSeatAnswer.Declined;
            }
            catch (Exception ex)
            {
                _log.Info("{PlayerId} did not answer the seat reservation ({Error}); its seat is treated as lost", ticket.PlayerId, ex.GetType().Name);
                return MatchmakingSeatAnswer.TimedOut;
            }
        }

        /// <summary> Tell one account what it is owed, per the dissolved-pairing table. </summary>
        void ApplySeatAction(MatchmakingTicket ticket, FormationSeatAction action, EntityId matchId, int seat)
        {
            switch (action)
            {
                case FormationSeatAction.Seated:
                    CastMessage(ticket.PlayerId, new InternalMatchmakingFormed(matchId, seat, ticket.DeckChoice));
                    break;

                case FormationSeatAction.Requeue:
                    // With its original arrival stamp, so it keeps the band it earned. Removed first, in case the
                    // account re-entered while this formation was in flight.
                    RemoveFromQueue(ticket.PlayerId);
                    _queue.Add(ticket);
                    CastMessage(ticket.PlayerId, new InternalMatchmakingReservationReleased());
                    break;

                case FormationSeatAction.TellSeatGone:
                    CastMessage(ticket.PlayerId, new InternalMatchmakingSeatGone());
                    break;

                case FormationSeatAction.Nothing:
                    break;
            }
        }

        // ---------------------------------------------------------------- what the table is born with

        /// <summary>
        /// Two humans, each seated exactly as their ticket froze at enqueue. Turn order is drawn inside the
        /// table from its deal seed (<c>Docs/hidden-information.md</c>).
        /// </summary>
        MatchSetupParams BuildPairSetup(MatchmakingTicket a, MatchmakingTicket b, SharedGameConfig config)
        {
            StakesTier tier = StakesTierPolicy.ComputeTier(
                StakesSeatFacts.OfTicket(a),
                StakesSeatFacts.OfTicket(b),
                config.Global);

            MatchStakes stakes = new MatchStakes(
                isRanked: true,
                tier,
                new List<int> { a.PowerScore, b.PowerScore },
                new List<List<CardId>> { a.Seat.LockedCards, b.Seat.LockedCards });

            return new MatchSetupParams(new List<MatchSeatSetup> { a.Seat, b.Seat }, stakes);
        }

        /// <summary>
        /// One human and the fill-wait bot, which brings a starter deck off the waiter's clan pair at ranks near
        /// the waiter's Power Score. The human's rating moves; no Heist runs.
        /// </summary>
        MatchSetupParams BuildBotSetup(MatchmakingTicket waiter, SharedGameConfig config)
        {
            BotProfileId profile = FallbackBotPolicy.ProfileFor(waiter.Rating, config.Global, Options.ToSchedule());

            List<CardId>                botDeck  = BotDecks.DeckForOpponent(config, waiter.Seat.Deck, (int)profile).ToCardIds();
            int                         botRank  = BotDecks.RankForPowerScore(config, waiter.PowerScore);
            MetaDictionary<CardId, int> botRanks = BotDecks.UniformRanks(botDeck, botRank);
            int                         botScore = DeckValidator.ComputePowerScore(botDeck, botRanks);

            // Ranked at the practice tier: rating moves, and with no collection across the table no rank can.
            MatchStakes stakes = new MatchStakes(
                isRanked: true,
                StakesTier.Practice,
                new List<int> { waiter.PowerScore, botScore },
                new List<List<CardId>> { waiter.Seat.LockedCards, new List<CardId>() });

            List<MatchSeatSetup> seats = new List<MatchSeatSetup>
            {
                waiter.Seat,
                new MatchSeatSetup(
                    EntityId.None, BotDecks.NameFor(profile), SeatOccupancy.Bot, profile,
                    botDeck, botRanks, new List<CardId>(),
                    // The bot counts as the waiter's own rating, so the fallback is rating-neutral in expectation.
                    waiter.Rating),
            };

            return new MatchSetupParams(seats, stakes);
        }
    }
}
