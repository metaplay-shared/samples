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
    /// <summary>
    /// A bot's state in the same loop a player follows: the menu, a search, a table, and back to the menu when
    /// the game ends.
    /// </summary>
    public enum BotClientState
    {
        /// <summary>No session yet.</summary>
        Connecting,

        /// <summary>Has a session but no table. The next tick asks for a game.</summary>
        AtTheMenu,

        /// <summary>Waiting for the matchmaker.</summary>
        Searching,

        /// <summary>At a table, playing its seat.</summary>
        Playing,
    }

    [EntityConfig]
    internal class BotClientConfig : BotClientConfigBase
    {
        public override Type EntityActorType => typeof(BotClient);
    }

    /// <summary>
    /// A load-testing bot that plays Table Stakes like a player: it enters matchmaking, takes the seat it is
    /// given, plays a card on its turn, and asks for another game when the table ends. It uses the same
    /// messages, match sub-client and replicated model as the browser client.
    /// <para>
    /// It is not related to the bots that cover seats in a match. To the server, a load bot's seat is a
    /// <b>human</b> seat, so it exercises the same server paths as a player: the matchmaker's connection check,
    /// the join window, the move deadline, and result delivery to the player actor.
    /// </para>
    /// </summary>
    public class BotClient : BotClientBase
    {
        /// <summary>
        /// The think delays this bot uses before playing a card. They shape the load and do not affect the game:
        /// the seat is a human seat, so the server's bot timing options do not apply to it. The values are the
        /// game's defaults so that the load resembles people playing at a normal pace.
        /// </summary>
        static readonly MatchTimings SeatPacing = MatchTimings.Default;

        /// <summary>
        /// The sub-clients, allocated in a field initializer because the base constructor reads
        /// <see cref="AdditionalSubClients"/> twice before this class's constructor body runs. A getter that
        /// allocated a new array would give the client store one instance and this bot another.
        /// </summary>
        readonly IMetaplaySubClient[] _subClients = new IMetaplaySubClient[] { new MatchClient() };

        protected override IMetaplaySubClient[] AdditionalSubClients => _subClients;

        MatchClient Match => (MatchClient)_subClients[0];

        BotClientState State { get; set; } = BotClientState.Connecting;

        /// <summary>Random source for this bot's profile, card choice and think delay.</summary>
        readonly RandomPCG _rng = RandomPCG.CreateNew();

        /// <summary>
        /// How this bot plays. Drawn once per session from the bot profiles in the game config, so load bots at
        /// one table play differently, and sessions that start after a config change use the new profiles.
        /// </summary>
        BotProfile _profile;

        /// <summary>
        /// The play index this bot has already sent a card for, so it sends one move per turn instead of one per
        /// tick while waiting for the answer. When a refusal or an auto-play advances the board, the play index
        /// changes and the bot can play again.
        /// </summary>
        int _sentAtPlayIndex = -1;

        /// <summary>
        /// When this bot will play its card, or <see cref="MetaTime.Epoch"/> if no delay has been drawn for the
        /// current turn. Playing on the same tick as the turn arrives would leave the server's timers out of the
        /// load, so the delay is drawn the same way as for the game's own bots.
        /// </summary>
        MetaTime _playAt = MetaTime.Epoch;

        /// <summary>
        /// When the bot stops waiting in its current state. Every wait has a limit, because a stuck bot would
        /// generate no load and report no failure.
        /// </summary>
        MetaTime _waitUntil = MetaTime.Epoch;

        /// <summary>How long to wait for the matchmaker before asking again.</summary>
        static readonly MetaDuration SearchTimeout = MetaDuration.FromSeconds(60);

        /// <summary>How long the bot waits at a table for its turn before it leaves the table.</summary>
        static readonly MetaDuration TurnWaitTimeout = MetaDuration.FromMinutes(5);

        /// <summary>How long the bot waits after a game, or after a failed search, before asking for a game.</summary>
        static readonly MetaDuration PauseBeforeNextSearch = MetaDuration.FromSeconds(3);

        protected override string GetCurrentStateLabel() => State.ToString();

        protected override Task OnSessionStartedAsync(BotSessionStartedArgs args)
        {
            // The match sub-client attaches after this method returns, so whether the bot is at a table is not
            // known yet. The update loop reads the sub-client's phase and finds a restored table.
            _profile = BotProfiles.DrawForSeat(BotConfig.DrawableProfiles(args.GameConfig as SharedGameConfig), _rng.NextULong(), seat: 0);
            EnterState(BotClientState.AtTheMenu, MetaDuration.Zero);

            // Claim the daily reward first, as a returning player does. The claim enqueues a synchronized server
            // action that changes the checksummed wallet, which loads that path. A bot reconnects several times a
            // day, so all but the first claim of a day also load the refusal path.
            SendToServer(PlayerDailyRewardClaimRequest.Instance);

            return Task.CompletedTask;
        }

        protected override Task OnNetworkMessage(MetaMessage message)
        {
            switch (message)
            {
                case MatchmakingStatusUpdate update:
                    OnMatchmakingStatus(update.Status);
                    return Task.CompletedTask;

                case PlayerDailyRewardClaimResponse claim:
                    // Only logged, because the bot's loop does not depend on the reward. The log shows each
                    // refusal next to the earlier grant for the same day.
                    _log.Debug("Daily reward for day {Activation}: {Outcome}.",
                        claim.ActivationIndex, claim.IsAccepted() ? "granted" : claim.Refusal.ToString());
                    return Task.CompletedTask;

                default:
                    _log.Warning("Unknown message received: {Message}", PrettyPrint.Compact(message));
                    return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Handles a matchmaking status update. <see cref="MatchmakingStatus.Unavailable"/> and
        /// <see cref="MatchmakingStatus.NotSearching"/> send the bot back to the menu to ask again after
        /// <see cref="PauseBeforeNextSearch"/>. Being seated is not handled here: the game starts when the table's
        /// match sub-client becomes active.
        /// </summary>
        void OnMatchmakingStatus(MatchmakingStatus status)
        {
            if (State != BotClientState.Searching)
                return;

            if (status == MatchmakingStatus.Unavailable || status == MatchmakingStatus.NotSearching)
            {
                _log.Debug("The matchmaker answered {Status}; asking again shortly.", status);
                EnterState(BotClientState.AtTheMenu, PauseBeforeNextSearch);
            }
        }

        protected override Task OnUpdate()
        {
            MetaTime now = MetaTime.Now;

            switch (State)
            {
                case BotClientState.AtTheMenu:
                    TickAtTheMenu(now);
                    break;

                case BotClientState.Searching:
                    TickSearching(now);
                    break;

                case BotClientState.Playing:
                    TickPlaying(now);
                    break;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Asks for a game, unless the bot is already at a game in progress. A session that reconnected to a
        /// running match plays it out. The server would refuse matchmaking anyway while the match pointer is set.
        /// </summary>
        void TickAtTheMenu(MetaTime now)
        {
            if (IsAtAGameInProgress)
            {
                EnterState(BotClientState.Playing, TurnWaitTimeout);
                return;
            }

            if (now < _waitUntil)
                return;

            SendToServer(new MatchmakingEnterRequest());
            EnterState(BotClientState.Searching, SearchTimeout);
        }

        void TickSearching(MetaTime now)
        {
            if (IsAtAGameInProgress)
            {
                EnterState(BotClientState.Playing, TurnWaitTimeout);
                return;
            }

            if (now >= _waitUntil)
            {
                _log.Warning("No table after {Timeout} of searching; asking again.", SearchTimeout);
                EnterState(BotClientState.AtTheMenu, MetaDuration.Zero);
            }
        }

        /// <summary>
        /// One tick at a table: returns to the menu if the table is gone or the game has ended, leaves the table
        /// after <see cref="TurnWaitTimeout"/> without a turn, and otherwise plays the seat's turn.
        /// </summary>
        void TickPlaying(MetaTime now)
        {
            MatchModel match = Match.Model;
            if (Match.Phase != MultiplayerEntityClientPhase.EntityActive || match == null)
            {
                EnterState(BotClientState.AtTheMenu, MetaDuration.Zero);
                return;
            }

            if (match.IsFinished)
            {
                // Pause on the results, then ask for another game, so the matchmaker stays under load.
                EnterState(BotClientState.AtTheMenu, PauseBeforeNextSearch);
                return;
            }

            if (now >= _waitUntil)
            {
                _log.Warning("No turn at this table for {Timeout}; leaving it.", TurnWaitTimeout);
                Match.Leave();
                EnterState(BotClientState.AtTheMenu, PauseBeforeNextSearch);
                return;
            }

            TickOwnTurn(match, now);
        }

        void TickOwnTurn(MatchModel match, MetaTime now)
        {
            // Check the board first, because building a seat view allocates the view and two card lists, and
            // on most ticks it is not this bot's turn. Otherwise the load run would mostly measure those
            // allocations.
            MatchBoard board = match.Board;
            if (match.OwnSeat < 0 || board.SeatOnTurn != match.OwnSeat)
                return;

            int playIndex = board.PlayIndex;
            if (playIndex == _sentAtPlayIndex)
                return;

            // Still thinking about this turn. Every later check would return without changing anything, so skip
            // building the seat view on each tick of the think delay.
            if (_playAt != MetaTime.Epoch && now < _playAt)
                return;

            MatchSeatView view = match.GetOwnSeatView();
            if (view == null || !view.IsOnTurn)
                return;

            // A new turn: draw a think delay as the game's own bots do, and reset the table timeout.
            if (_playAt == MetaTime.Epoch)
            {
                _playAt    = now + BotPolicy.DrawThinkDelay(view, _profile, SeatPacing, _rng.NextULong());
                _waitUntil = now + TurnWaitTimeout;
                return;
            }

            if (now < _playAt)
                return;

            List<Card> legal = view.GetLegalPlays();
            if (legal.Count == 0)
            {
                _log.Warning("On turn at play index {PlayIndex} with no legal card in hand.", playIndex);
                return;
            }

            Match.PlayCard(BotPolicy.ChooseCard(view, _profile, _rng.NextULong()));
            _sentAtPlayIndex = playIndex;
            _playAt          = MetaTime.Epoch;
        }

        /// <summary>
        /// Whether the bot is at a game that has not finished. A <i>finished</i> table stays attached for the rest
        /// of the session, which keeps the results on screen in the browser, so checking only the attachment
        /// would make the bot stop after its first game. The browser's Play again button also asks for a new
        /// game while the finished table is attached.
        /// </summary>
        bool IsAtAGameInProgress
            => Match.Phase == MultiplayerEntityClientPhase.EntityActive && Match.Model != null && !Match.Model.IsFinished;

        /// <summary>
        /// Moves to <paramref name="state"/> and sets how long the bot may wait in it. Clears the pending move,
        /// because a move for a table the bot has left refers to a play index that no longer applies.
        /// </summary>
        void EnterState(BotClientState state, MetaDuration wait)
        {
            State            = state;
            _waitUntil       = MetaTime.Now + wait;
            _sentAtPlayIndex = -1;
            _playAt          = MetaTime.Epoch;
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
