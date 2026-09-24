using Game.Logic;
using Metaplay.BotClient;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.BotClient
{
    public enum BotClientState
    {
        Connecting,
        Main,
    }

    // BotClient

    [EntityConfig]
    internal class BotClientConfig : BotClientConfigBase
    {
        public override Type EntityActorType => typeof(BotClient);
    }

    /// <summary>
    /// Load-testing client. It builds a deck, enters a practice match and plays it — and the point is that
    /// <b>its seat is a human seat</b>. A load bot logs in over the real transport, is seated by the real
    /// entry path, and plays over the network, so it exercises the paths a person's seat takes: the subscribe
    /// handshake, the private-state hand delivery, the intent round trip and the result delivery to a player
    /// actor. None of those is touched by a seat the match actor plays itself
    /// (<c>Docs/bots.md</c>).
    /// <para>
    /// What it borrows from the design's bot doctrine is only the action-choosing policy, so it plays like an
    /// opponent rather than hammering the socket as fast as it will go.
    /// </para>
    /// </summary>
    public class BotClient : BotClientBase
    {
        BotClientState State { get; set; } = BotClientState.Connecting;

        readonly RandomPCG _rnd = RandomPCG.CreateNew();

        PlayerModel      _playerModel => (PlayerModel)PlayerContext.Model;
        SharedGameConfig _gameConfig;

        /// <summary> The strongest profile over this content: what plays this bot's own seat. </summary>
        BotPolicy _policy;

        /// <summary>
        /// The match sub-client, registered alongside the player context at startup. Held in a field because
        /// the base reads <see cref="AdditionalSubClients"/> once, during construction.
        /// </summary>
        readonly BotMatchClient _matchClient = new BotMatchClient();

        /// <summary> The action count the last intent was sent at, so one is in flight at a time. </summary>
        int? _sentAt;

        /// <summary> The request id of that intent, to recognise its refusal. </summary>
        int _sentRequestId;

        int _nextRequestId = 1;

        protected override string GetCurrentStateLabel() => State.ToString();

        /// <summary>
        /// The match sub-client rides on its own client slot, exactly as the browser client's does. The
        /// association arriving on that slot is what seats this bot; it is never told to go find a match.
        /// </summary>
        protected override IMetaplaySubClient[] AdditionalSubClients => new IMetaplaySubClient[] { _matchClient };

        protected override Task OnUpdate()
        {
            switch (State)
            {
                case BotClientState.Main:
                    TickMainState();
                    break;
            }

            return Task.CompletedTask;
        }

        protected override Task OnNetworkMessage(MetaMessage message)
        {
            // The queue's two directed messages ride the player's own session rather than an entity channel,
            // so this is where they land. The status push is only the searching dialog's countdown and this
            // client draws nothing; the end is what says the ticket is gone, and taking the server's word for
            // it is better than the optimistic flag, which cannot see a refusal or a lost seat.
            if (message is MatchmakingStatusUpdate)
                return Task.CompletedTask;

            if (message is MatchmakingEnded ended)
            {
                _log.Debug("The search ended: {Reason}", ended.Reason);
                _searching = false;
                return Task.CompletedTask;
            }

            // The base class handles the session and player-action messages before they reach here; the match
            // channel's directed messages arrive through the sub-client's own dispatcher.
            _log.Warning("Unknown message received: {Message}", PrettyPrint.Compact(message));

            return Task.CompletedTask;
        }

        protected override Task OnSessionStartedAsync(BotSessionStartedArgs args)
        {
            _gameConfig = (SharedGameConfig)args.GameConfig;
            _policy     = BotPolicy.Strongest(_gameConfig);
            State       = BotClientState.Main;

            // A new session means no ticket: the account's own actor takes a searching player out of the queue
            // when their session ends, so a flag carried across one would have this bot waiting on nothing.
            _searching  = false;

            return Task.CompletedTask;
        }

        void TickMainState()
        {
            if (_matchClient.Context != null)
            {
                _searching = false;
                PlayOneAction();
                return;
            }

            WaiveTheNewcomerShield();

            // Out of match: keep the player timeline busy, then queue again.
            if (_rnd.NextInt(1000) < 5)
                PlayerContext.ExecuteAction(new PlayerSetDisplayName($"Bot {_rnd.NextInt(100_000)}"));

            // Occasionally leave the queue again. It is the only cheap way to put load on the
            // cancel-versus-formation race, which is otherwise a window a few milliseconds wide that no
            // ordinary run ever lands in.
            if (_searching)
            {
                if (_rnd.NextInt(1000) < 8)
                {
                    PlayerContext.ExecuteAction(new PlayerCancelMatchmaking());
                    _searching = false;
                }

                return;
            }

            if (_rnd.NextInt(1000) < 20)
                EnterAMatch();
        }

        /// <summary>
        /// Whether this bot has a ticket in the queue as far as it knows. Optimistic and approximate on
        /// purpose: the server is the arbiter, and a bot that guessed wrong simply has a cancel or an entry
        /// refused. Guessing is what a load client is for.
        /// </summary>
        bool _searching;

        /// <summary>
        /// Lift this account's own newcomer shield, once. <b>Without it a load run cannot reach the Heist at
        /// all</b>: a shield on <em>either</em> seat shields the match, every bot account is fresh, and the
        /// shield covers the first ten ranked games — so two bots would pair for the whole run at a tier that
        /// moves no ranks, and the phase, the pick clock and the rank transfer would never be exercised under
        /// load.
        /// <para>
        /// It goes through the development-only action rather than the dashboard's: the dashboard's is a
        /// server action and a client cannot send one. Both are refused outside a development environment,
        /// which is the only place a load run is ever pointed.
        /// </para>
        /// </summary>
        void WaiveTheNewcomerShield()
        {
            if (_playerModel == null || _playerModel.NewcomerShieldWaived)
                return;

            PlayerContext.ExecuteAction(new PlayerDevWaiveNewcomerShield());
        }

        /// <summary>
        /// Save a legal deck if there is none, then enter a match. <b>Ranked by default</b>, because the
        /// matchmaker's real pairing path is what a load run is here to exercise and nothing else in the
        /// sample reaches it at volume: bots queue, pair with each other, and occasionally cancel. Practice is
        /// still taken sometimes, so the direct-mint path keeps its cover too.
        /// <para>
        /// The deck is built by the same shared validator the deckbuilder predicts with, so a load run cannot
        /// be seated with a list the server would refuse.
        /// </para>
        /// </summary>
        void EnterAMatch()
        {
            if (_playerModel.Decks.Count == 0)
            {
                List<CardId> deck = BuildLegalDeck();
                if (deck == null)
                    return;

                PlayerContext.ExecuteAction(new PlayerSaveDeck(null, "Load", deck));
                return;
            }

            foreach ((int deckId, PlayerDeck _) in _playerModel.Decks)
            {
                if (_rnd.NextInt(100) < 20)
                {
                    PlayerContext.ExecuteAction(new PlayerStartPracticeMatch(DeckChoice.Saved(deckId), BotProfileId.Practiced));
                    return;
                }

                PlayerContext.ExecuteAction(new PlayerEnqueueForRankedMatch(DeckChoice.Saved(deckId)));
                _searching = true;
                return;
            }
        }

        /// <summary> A legal 25-card list out of what this account owns: Wanderers first, then whole clans. </summary>
        List<CardId> BuildLegalDeck()
        {
            GlobalConfig global = _gameConfig.Global;

            List<CardId>       wanderers = new List<CardId>();
            List<List<CardId>> byClan    = new List<List<CardId>>();
            List<ClanId>       clanOrder = new List<ClanId>();

            foreach ((CardId cardId, CardInfo card) in _gameConfig.Cards)
            {
                if (!card.Collectible || !_playerModel.Collection.ContainsKey(cardId))
                    continue;

                ClanInfo clan = card.Clan.Ref;
                if (!clan.CountsTowardClanLimit)
                {
                    wanderers.Add(cardId);
                    continue;
                }

                int ndx = clanOrder.IndexOf(clan.ClanId);
                if (ndx < 0)
                {
                    clanOrder.Add(clan.ClanId);
                    byClan.Add(new List<CardId>());
                    ndx = clanOrder.Count - 1;
                }

                byClan[ndx].Add(cardId);
            }

            List<CardId> deck = new List<CardId>(global.DeckSize);
            foreach (CardId cardId in wanderers)
            {
                if (deck.Count >= global.DeckSize)
                    break;
                deck.Add(cardId);
            }

            for (int clan = 0; clan < clanOrder.Count && clan < global.MaxClansPerDeck; clan++)
            {
                foreach (CardId cardId in byClan[clan])
                {
                    if (deck.Count >= global.DeckSize)
                        break;
                    deck.Add(cardId);
                }
            }

            return deck.Count == global.DeckSize ? deck : null;
        }

        /// <summary>
        /// One action, through the same directed-intent path a person's client uses. One intent at a time,
        /// guarded on the action count it was sent at: the second action's legality depends on the first's
        /// result, so a queued one would answer a board the server has already moved past.
        /// </summary>
        void PlayOneAction()
        {
            BotMatchClientContext context = _matchClient.Context;
            MatchModel         model   = context.CommittedModel;

            if (model.Rules == null || model.OwnHand == null)
                return;

            int seat = model.SeatIndexOfPlayer(_playerModel.PlayerId);
            if (seat < 0 || model.Result != null)
                return;

            // One intent in flight per action count. A refusal leaves the count where it was, so its landing is
            // what frees the bot to choose again; otherwise it would idle until the deadline played its turn.
            if (_sentAt.HasValue && _sentAt.Value == model.Rules.ActionCount)
            {
                if (context.LastRefusal == null || context.LastRefusal.RequestId != _sentRequestId)
                    return;

                _log.Debug("Intent {RequestId} was refused ({Reason}); choosing again", _sentRequestId, context.LastRefusal.Reason);
            }

            MatchIntent intent = ChooseIntent(model, seat);
            if (intent == null)
                return;

            _sentAt        = model.Rules.ActionCount;
            _sentRequestId = _nextRequestId++;
            context.Send(new MatchIntentMessage(intent, _sentRequestId));
        }

        /// <summary>
        /// What to send, decided from the replicated model and this seat's own delivered hand with the shared
        /// legality walk — which is literally the same walk the server's own gate runs,
        /// with the hand as its one argument.
        /// <para>
        /// This client is also the only place a <b>real</b> follower re-executes the rule actions, as opposed
        /// to the offline mirror in the test suite — its own model is built by replaying the timeline the
        /// actor wrote. So a divergence the mirror somehow missed fails a load run loudly: the bot client's
        /// desync hook throws rather than reconnecting quietly.
        /// </para>
        /// </summary>
        MatchIntent ChooseIntent(MatchModel model, int seat)
        {
            if (model.Rules.Phase == MatchPhase.Mulligan)
            {
                if (model.Rules.Seat(seat).HasMulliganed)
                    return null;

                // Keep the dealt hand: what a load run is measuring is the round trip, not the curve.
                return new MulliganIntent(new List<CardInstanceId>());
            }

            // A held peek is answered by the policy's own keep rule; the legal set is empty while it is held.
            bool answeringPeek = model.OwnPeek != null;
            if (!answeringPeek && (model.Rules.Phase != MatchPhase.Playing || model.Rules.SeatOnTurn != seat))
                return null;

            // One walk, with this client's own delivered hand as the argument. The server passes the seat's
            // secret hand to the same function.
            List<MatchIntent> legal = answeringPeek ? new List<MatchIntent>() : Legality.EnumerateLegalIntents(model, seat, model.OwnHand);
            if (!answeringPeek && legal.Count == 0)
                return null;

            // The policy's seat view is built from this client's own model: the public members plus the hand
            // and peek the private channel delivered.
            SeatView view = new SeatView(
                seat, model.Rules, model.Pacing, model.Timings, model.Seats, model.Stakes,
                model.OwnHand, model.OwnPeek, legal, MatchPending.Of(model));

            return _policy.ChooseAction(view, seat) ?? new EndTurnIntent();
        }

        [MessageHandler]
        void HandleInitializeBot(BotCoordinator.InitializeBot _)
        {
            _log.Debug("Initializing bot");
        }

        static async Task<int> Main(string[] cmdLineArgs)
        {
            using (BotClientMain program = new BotClientMain())
                return await program.RunBotsAsync(cmdLineArgs);
        }
    }
}
