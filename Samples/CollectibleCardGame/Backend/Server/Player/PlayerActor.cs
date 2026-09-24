using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Game.Server.Player
{
    [EntityConfig]
    public class PlayerConfig : PlayerConfigBase
    {
        public override Type EntityActorType => typeof(PlayerActor);
    }

    /// <summary>
    /// Server-side entity actor representing a single player. Owns the authoritative <see cref="PlayerModel"/>
    /// and reacts to player actions via <see cref="IPlayerModelServerListener"/>. It is also the account's end
    /// of the match: it mints a practice table, keeps the match pointer, re-attaches a returning player, and
    /// folds a result in exactly once.
    /// </summary>
    public sealed partial class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        /// <summary>
        /// The specialized SharedGameConfig (with any A/B experiment overrides) for this player. Only available
        /// after PostLoad - i.e. in most methods outside Initialize, InitializePersisted, PostLoad and the ctor.
        /// </summary>
        SharedGameConfig SharedGameConfig => (SharedGameConfig)_specializedGameConfig.SharedConfig;

        protected override string RandomNewPlayerName()
        {
            return Invariant($"Guest {new Random().Next(100_000)}");
        }

        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }

        void IPlayerModelServerListener.OnDisplayNameChanged(string displayName)
        {
            // Example server-side reaction to a player action: here we just log, but this is where analytics
            // events, notifications to other entities, etc. would go. Server listeners must not mutate model state.
            _log.Info("Player display name changed to {DisplayName}", displayName);
            PublishLeaderboardEntry();
        }

        // ---------------------------------------------------------------- the match pointer

        /// <summary>
        /// Re-attach the account to the match it is in, if any, by the association arriving on its match slot.
        /// This runs on every session start, so a reconnect and a server restart take the same path.
        /// </summary>
        protected override async Task OnClientSessionHandshakeAsync(PlayerSessionParams sessionParams)
        {
            PublishLeaderboardEntry();
            if (Model.CurrentMatch == EntityId.None)
                return;

            EntityId matchId = Model.CurrentMatch;

            // A table exists exactly as long as its actor, so a pointer can name one that is gone. Asking first
            // turns that into "you are on Home with a notice" instead of a session start that fails.
            if (await ProbeMatchAsync(matchId) == MatchProbeVerdict.Gone)
            {
                _log.Info("Match {MatchId} is gone; clearing the pointer and telling the account", matchId);
                Model.CurrentMatch = EntityId.None;
                EnqueueServerAction(new PlayerNoteMatchGone());
                await PersistStateIntermediate();
                return;
            }

            AddEntityAssociation(new AssociatedEntityRefBase.Default(ClientSlotGame.Match, _entityId, matchId), removeOnSessionEnd: true);
        }

        /// <summary>
        /// The second belt, for a table that is <b>alive</b> but no longer holds a seat for this account (queue
        /// re-formation, an expired join window). Without clearing the pointer here every later login would
        /// re-fail. Clearing applies nothing; a live table goes on retrying its result, applied idempotently.
        /// </summary>
        protected override Task<bool> OnAssociatedEntityRefusalAsync(AssociatedEntityRefBase association, InternalEntitySubscribeRefusedBase refusal)
        {
            if (association.GetClientSlot() == ClientSlotGame.Match)
            {
                _log.Warning("Match {MatchId} refused the association ({Refusal}); clearing the pointer", association.AssociatedEntity, refusal.GetType().Name);
                Model.CurrentMatch = EntityId.None;
                EnqueueServerAction(new PlayerNoteMatchGone());
                return Task.FromResult(true);
            }

            return base.OnAssociatedEntityRefusalAsync(association, refusal);
        }

        enum MatchProbeVerdict
        {
            Alive,
            Gone,
            Unreachable,
        }

        /// <summary>
        /// Shorter than the SDK's ten-second ask default, because the session-start budget this runs inside is
        /// also ten seconds.
        /// </summary>
        static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Ask the table whether it is set up. <b>A failed ask is not a "no"</b>: a timeout or a routing
        /// failure keeps the pointer, because a cold shard looks the same as a lost node from here and
        /// clearing a live table's pointer throws its player out mid-turn. Only an explicit
        /// <c>IsSetUp: false</c> means gone; the association refusal is the belt for a table that really is.
        /// </summary>
        async Task<MatchProbeVerdict> ProbeMatchAsync(EntityId matchId)
        {
            try
            {
                // An ask to an id nobody set up spawns a fresh actor with a null model, and it answers anyway.
                InternalMatchProbeResponse response = await EntityAskAsync(matchId, new InternalMatchProbeRequest(), ProbeTimeout);
                return response.IsSetUp ? MatchProbeVerdict.Alive : MatchProbeVerdict.Gone;
            }
            catch (Exception ex)
            {
                _log.Info("Match {MatchId} did not answer the probe ({Error}); keeping the pointer", matchId, ex.GetType().Name);
                return MatchProbeVerdict.Unreachable;
            }
        }

        // ---------------------------------------------------------------- practice entry

        void IPlayerModelServerListener.StartPracticeMatch(DeckChoice deck, BotProfileId botProfile)
            => EnqueueOnActorContext(() => TryStartPracticeMatchAsync(deck, botProfile));

        /// <summary>
        /// <c>EnqueueOnActorContext</c> crashes the actor on an unhandled exception, and the mint is an entity
        /// ask against a shard that may be cold. Refusing to start is the only safe failure.
        /// </summary>
        async Task TryStartPracticeMatchAsync(DeckChoice deck, BotProfileId botProfile)
        {
            try
            {
                await StartPracticeMatchAsync(deck, botProfile);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Starting a practice match failed; the account keeps no pointer and stays free to try again");
            }
        }

        /// <summary>
        /// Mint a practice table and point the account at it. <b>Mint before assigning</b>
        /// (<c>Docs/protocol.md</c>): the other order would point a player at a table that does not exist.
        /// </summary>
        async Task StartPracticeMatchAsync(DeckChoice deck, BotProfileId botProfile)
        {
            if (Model.IsInMatch)
            {
                _log.Info("Not starting a practice match: the account is already at table {MatchId}", Model.CurrentMatch);
                return;
            }

            // One table per account, and a queue ticket is a claim on the next seat: minting under it would let
            // the matchmaker pair a player it can no longer seat. Refused silently, because a MatchmakingEnded
            // here would close a searching dialog that is legitimately up.
            if (_searchPhase != MatchmakingSearchPhase.None)
            {
                _log.Info("Not starting a practice match: the account is in the ranked queue ({Phase})", _searchPhase);
                return;
            }

            // Looked up again: a deck can be deleted, or dropped from the config, after the action ran.
            DeckChoiceResult resolved = DeckChoiceResolver.Resolve(deck, Model);
            if (!resolved.IsValid)
            {
                _log.Warning("Not starting a practice match: deck {Deck} no longer exists ({Error})", deck, resolved.Error);
                return;
            }

            MatchSetupParams setup   = BuildPracticeSetup(resolved.Cards, botProfile);
            EntityId         matchId = await MatchMinting.MintAsync(
                id => EntityAskAsync(id, new InternalEntitySetupRequest(setup)),
                id => EntityAskAsync(id, new InternalMatchAbandonRequest(), TimeSpan.FromSeconds(2)),
                _log);

            if (!matchId.IsValid)
            {
                _log.Warning("Not starting a practice match: no table could be minted");
                return;
            }

            Model.CurrentMatch = matchId;
            AddEntityAssociation(new AssociatedEntityRefBase.Default(ClientSlotGame.Match, _entityId, matchId), removeOnSessionEnd: true);

            // Recorded here rather than in the entry action: whether the account was seated depends on
            // ServerOnly state, so a predicted write would desync the public member.
            EnqueueServerAction(new PlayerNoteDeckPlayed(deck));

            await PersistStateIntermediate();

            _log.Info("Seated at practice table {MatchId} with deck {Deck} against {Profile}", matchId, deck, botProfile);
        }

        /// <summary>
        /// Freeze everything the table is born with: this account's seat, and a bot whose deck sits near the same
        /// Power Score. Practice is unranked, at the practice tier, with no Heist.
        /// </summary>
        MatchSetupParams BuildPracticeSetup(IReadOnlyList<CardId> deck, BotProfileId botProfile)
        {
            SharedGameConfig config = SharedGameConfig;
            (MatchSeatSetup human, int powerScore) = MatchmakingTicketPolicy.FreezeSeat(Model, _entityId, deck, config);

            List<CardId>                botDeck  = BotDecks.DeckForOpponent(config, human.Deck, (int)botProfile).ToCardIds();
            int                         botRank  = BotDecks.RankForPowerScore(config, powerScore);
            MetaDictionary<CardId, int> botRanks = BotDecks.UniformRanks(botDeck, botRank);
            int                         botScore = DeckValidator.ComputePowerScore(botDeck, botRanks);

            List<MatchSeatSetup> seats = new List<MatchSeatSetup>
            {
                human,
                // The bot carries the account's own rating, which practice never reads.
                new MatchSeatSetup(EntityId.None, BotDecks.NameFor(botProfile), SeatOccupancy.Bot, botProfile, botDeck, botRanks, new List<CardId>(), human.Rating),
            };

            MatchStakes stakes = MatchStakes.Practice(
                new List<int> { powerScore, botScore },
                new List<List<CardId>> { human.LockedCards, new List<CardId>() });

            return new MatchSetupParams(seats, stakes);
        }

        // ---------------------------------------------------------------- the result

        /// <summary>
        /// Fold one table's outcome into this account, once. The table re-delivers until acknowledged, and this
        /// is idempotent on the match id, held in a <c>ServerOnly</c> member. The record and the <b>rank
        /// transfer</b> go through a synchronized server action, because both are checksummed state; each
        /// account applies its own side of the same picks, derived from its seat.
        /// </summary>
        [EntityAskHandler]
        async Task<InternalMatchDeliverResultResponse> HandleInternalMatchDeliverResultRequest(InternalMatchDeliverResultRequest request)
        {
            if (!Model.AppliedMatchResults.Contains(request.MatchId))
            {
                Model.AppliedMatchResults.Add(request.MatchId);

                if (request.Kind == MatchTerminalKind.Result && request.Result != null)
                {
                    List<CardId> heistPicks = request.Result.Heist?.Picks;

                    EnqueueServerAction(new PlayerApplyMatchResult(
                        request.Result.OutcomeForSeat(request.Seat),
                        request.Result.WasRanked,
                        request.RatingDelta,
                        heistPicks));

                    _log.Info("Recorded match {MatchId}: {Outcome}, rating {Delta:+#;-#;0}, heist {Picks}",
                        request.MatchId, request.Result.OutcomeForSeat(request.Seat), request.RatingDelta,
                        heistPicks is { Count: > 0 } ? string.Join(", ", heistPicks) : "none");
                }
                else
                    _log.Info("Match {MatchId} ended with nothing to record; releasing the account", request.MatchId);
            }

            // The pointer is cleared on either terminal kind. The association is not: it is what the player is
            // looking at, and removing it would close the board as the result comes up. It goes with the
            // session, or when the next match replaces it.
            if (Model.CurrentMatch == request.MatchId)
                Model.CurrentMatch = EntityId.None;

            await PersistStateIntermediate();

            return new InternalMatchDeliverResultResponse();
        }
    }
}
