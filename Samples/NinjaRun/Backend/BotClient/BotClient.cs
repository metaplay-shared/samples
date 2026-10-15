using Game.Logic;
using Metaplay.BotClient;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;
using System;
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

    public class BotClient : BotClientBase
    {
        /// <summary>Updates the bot waits between finishing one run and opening the next.</summary>
        const int UpdatesBetweenRuns = 20;

        BotClientState              State { get; set; } = BotClientState.Connecting;

        PlayerModel _playerModel => (PlayerModel)PlayerContext.Model;

        /// <summary>The ticket of the run the bot has submitted and is waiting on, or 0 when it is not waiting.</summary>
        int _submittedTicketId;

        /// <summary>Whether a start request is outstanding, so one is sent per run.</summary>
        bool _startRequested;

        /// <summary>
        /// The ticket that was open when the start request went out. The server answers by opening a new one,
        /// so this is how the bot tells the ticket it asked for from one left over from an earlier session.
        /// </summary>
        int _ticketIdBeforeStart;

        int _updatesUntilNextRun;

        protected override string GetCurrentStateLabel() => State.ToString();

        protected override Task OnUpdate()
        {
            // Tick current state (when connected)
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
            // The base class handles the session and player-action messages before they reach here.
            _log.Warning("Unknown message received: {Message}", PrettyPrint.Compact(message));

            return Task.CompletedTask;
        }

        protected override Task OnSessionStartedAsync(BotSessionStartedArgs args)
        {
            State                = BotClientState.Main;
            _submittedTicketId   = 0;
            _startRequested      = false;
            _ticketIdBeforeStart = 0;
            _updatesUntilNextRun = 0;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs one time trial per cycle against the server: open a run, play it with the scripted driver,
        /// submit the recording and wait for the verdict.
        /// </summary>
        void TickMainState()
        {
            if (_submittedTicketId != 0)
            {
                // Waiting for the server's verdict, which arrives as an accept or reject action on the model.
                if (_playerModel.ActiveTimeTrial == null || _playerModel.ActiveTimeTrial.TicketId != _submittedTicketId)
                {
                    _submittedTicketId   = 0;
                    _startRequested      = false;
                    _updatesUntilNextRun = UpdatesBetweenRuns;
                }
                return;
            }

            if (!_startRequested)
            {
                if (_updatesUntilNextRun > 0)
                {
                    _updatesUntilNextRun--;
                    return;
                }

                ClaimName();

                _startRequested      = true;
                _ticketIdBeforeStart = _playerModel.ActiveTimeTrial?.TicketId ?? 0;
                SendToServer(new TimeTrialStartRequest());
                return;
            }

            // The ticket arrives a round trip after the request, on the action the server enqueues.
            TimeTrialTicket ticket = _playerModel.ActiveTimeTrial;
            if (ticket == null || ticket.TicketId == _ticketIdBeforeStart)
                return;

            CourseDefinition course = CourseResolver.TryResolve(ticket.CourseId, _playerModel.GameConfig);
            if (course == null)
            {
                _log.Warning("The map {Course} the server opened names no map the published config composes", ticket.CourseId);
                _startRequested = false;
                return;
            }

            // Driven on the ticket's own Skill, so the recording the bot submits is one the server's replay of
            // that ticket reproduces.
            TimeTrialRecording recording = TimeTrialBotDriver.Drive(course, TimeTrialSkills.EffectiveRules(_playerModel.GameConfig, ticket.SkillId));
            _submittedTicketId = ticket.TicketId;

            // Actions are batched, and this message is not. Without the flush, the submission can reach the
            // server ahead of the action that opened the ticket it names, and be turned down for an attempt
            // the server has not heard of yet.
            PlayerContext.FlushActions();
            SendToServer(new TimeTrialSubmitRequest(ticket.TicketId, recording));
        }

        /// <summary>
        /// Buy the Name Tag as soon as the bot's Orbs cover it, then name the bot after its player id. A run
        /// made without a name is recorded into no course's pool, so this is what lets bots fill the pools
        /// and the time distributions a load test exercises.
        /// </summary>
        void ClaimName()
        {
            NameTagTutorialStep step = _playerModel.NameTagTutorialStep;
            if (step == NameTagTutorialStep.None)
                return;

            if (step == NameTagTutorialStep.BuyNameTag)
                PlayerContext.ExecuteAction(new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            PlayerContext.ExecuteAction(new PlayerSetName("Bot " + (_playerModel.PlayerId.Value % 10000).ToString()));
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
