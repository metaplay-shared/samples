using Game.Logic;
using Metaplay.Core;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// The strongest profile, which plays every seat on behalf of an absent human: auto-play, cover, the
        /// play-out and the peek's default. It reads no seed.
        /// </summary>
        BotPolicy _strongestPolicy;

        BotPolicy StrongestPolicy => _strongestPolicy ??= BotPolicy.Strongest(SharedConfig);

        /// <summary> Per seat, the policy that plays it when a personality is seated there. </summary>
        readonly BotPolicy[] _seatPolicies = new BotPolicy[MatchSeats.Count];

        // What the armed bot decision was armed for; it runs only if no action has been accepted since.
        MatchPendingKind _botArmedKind;
        int              _botArmedSeat = MatchSeats.None;
        int              _botArmedActionCount;

        // Where the current turn's think-delay budget started: the turn, and the action count when it was first
        // seen. Host state, because how long a bot has thought is pacing and not a rule.
        int _budgetTurn = -1;
        int _budgetFromActionCount;

        /// <summary>
        /// The policy for one seat. A covered seat gets the strongest profile; a seat seated as a bot may have
        /// a personality, keyed on the secret's bot seed rather than the deal seed.
        /// </summary>
        BotPolicy PolicyForSeat(int seat)
        {
            MatchSeat roster = Model.Seats[seat];

            if (roster.Occupancy != SeatOccupancy.Bot)
                return StrongestPolicy;

            BotProfile profile = BotProfiles.Resolve(roster.BotProfile);
            if (!profile.MakesMistakes)
                return StrongestPolicy;

            return _seatPolicies[seat] ??= new BotPolicy(SharedConfig, profile, SecretOps.BotSeedOf(Model));
        }

        /// <summary>
        /// Arm a bot seat's next decision if one is waiting, after a think delay budgeted across the whole turn
        /// (<c>Docs/bots.md</c>, "Pacing").
        /// </summary>
        void MaybeDriveBotSeat()
        {
            if (Model.IsTerminal || Model.Rules.Phase == MatchPhase.Complete)
                return;

            MatchPendingWork pending = MatchPending.Of(Model);
            if (pending.Kind == MatchPendingKind.Complete)
                return;

            int seat = NextBotSeatToDrive(pending);
            if (seat < 0)
            {
                // No bot owes anything, so a delay still armed (a seat reclaimed mid-delay) is stale and would
                // cost a wake for work that does not exist.
                ClearArmedBotDelay();
                return;
            }

            if (_botDelayAt.HasValue && _botArmedSeat == seat && _botArmedKind == pending.Kind && _botArmedActionCount == Model.Rules.ActionCount)
                return;

            _botArmedKind        = pending.Kind;
            _botArmedSeat        = seat;
            _botArmedActionCount = Model.Rules.ActionCount;
            _botDelayAt          = MetaTime.Now + NextBotDelay(pending);

            RearmFromModel();
        }

        /// <summary> Forget an armed bot decision, and re-arm so the fold stops offering its stamp. </summary>
        void ClearArmedBotDelay()
        {
            if (!_botDelayAt.HasValue)
                return;

            _botDelayAt          = null;
            _botArmedSeat        = MatchSeats.None;
            _botArmedKind        = MatchPendingKind.Complete;
            _botArmedActionCount = 0;

            RearmFromModel();
        }

        /// <summary>
        /// Which seat a bot owes an action for, or <see cref="MatchSeats.None"/>. During the mulligan the
        /// lowest-numbered bot seat that has not submitted goes first.
        /// </summary>
        int NextBotSeatToDrive(MatchPendingWork pending)
        {
            if (pending.Kind == MatchPendingKind.AwaitingMulligan)
            {
                for (int seat = 0; seat < MatchSeats.Count; seat++)
                {
                    if (Model.Seats[seat].IsBotDriven && !Model.Rules.Seat(seat).HasMulliganed)
                        return seat;
                }

                return MatchSeats.None;
            }

            if (pending.Seat < 0 || !Model.Seats[pending.Seat].IsBotDriven)
                return MatchSeats.None;

            return pending.Seat;
        }

        /// <summary> One action's think delay: the floor plus jitter while the turn's budget lasts, then the floor. </summary>
        MetaDuration NextBotDelay(MatchPendingWork pending)
        {
            MetaDuration floor = MetaDuration.FromTimeSpan(_options.BotActionDelay);
            if (floor <= MetaDuration.Zero)
                return MetaDuration.Zero;

            MetaDuration jitter = MetaDuration.FromTimeSpan(_options.BotActionDelayJitter);
            if (jitter <= MetaDuration.Zero)
                return floor;

            // A mulligan is one decision rather than a step in a sequence, so it takes the floor and no more.
            if (pending.Kind == MatchPendingKind.AwaitingMulligan)
                return floor;

            MetaDuration budget = MetaDuration.FromTimeSpan(_options.BotTurnBudget);
            if (_budgetTurn != Model.Rules.Turn)
            {
                _budgetTurn            = Model.Rules.Turn;
                _budgetFromActionCount = Model.Rules.ActionCount;
            }

            int          acted  = Model.Rules.ActionCount - _budgetFromActionCount;
            MetaDuration spent  = MetaDuration.FromMilliseconds(acted * floor.Milliseconds);

            if (spent >= budget)
                return floor;

            long jitterMs = _botJitter.NextInt((int)jitter.Milliseconds + 1);
            return floor + MetaDuration.FromMilliseconds(jitterMs);
        }

        /// <summary> The jitter source, deliberately outside the match's seeded stream. </summary>
        readonly RandomPCG _botJitter = RandomPCG.CreateNew();

        /// <summary>
        /// The armed decision came due. It is chosen now from the current state; a table that has moved is
        /// re-derived rather than reconciled (<c>Docs/bots.md</c>).
        /// </summary>
        void RunArmedBotAction()
        {
            _botDelayAt = null;

            int seat = _botArmedSeat;
            _botArmedSeat = MatchSeats.None;

            if (seat < 0 || Model.IsTerminal || Model.Rules.Phase == MatchPhase.Complete)
                return;

            MatchPendingWork pending = MatchPending.Of(Model);
            if (pending.Kind != _botArmedKind || Model.Rules.ActionCount != _botArmedActionCount)
            {
                // The table moved. Re-derive rather than submit something stale.
                MaybeDriveBotSeat();
                return;
            }

            if (!Model.Seats[seat].IsBotDriven)
                return;

            // The policy sees a seat view, never the model root, which holds the secret.
            SeatView    view   = SeatView.Build(Model, seat);
            MatchIntent intent = PolicyForSeat(seat).ChooseAction(view, seat) ?? new EndTurnIntent();

            // The same gate and path a human's intent takes.
            MatchIntentResult result = SubmitIntent(seat, intent);
            if (!result.IsSuccess)
                _log.Debug("Bot seat {Seat}'s {Intent} was refused {Result}", seat, intent.GetType().ToGenericTypeString(), result);

            AfterActions();
        }
    }
}
