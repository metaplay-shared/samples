using Metaplay.Core;

namespace Game.Server.Player
{
    /// <summary>
    /// The sector already drawn for a wheel spin whose synchronized action has not run yet. Enqueuing a
    /// <b>synchronized</b> server action does not change the actor's model until the client runs it, so a second
    /// request would find no pending receipt. It stores the drawn sector, so every repeated request for the
    /// same spin gets the same draw (<c>docs/spin-wheel.md</c>, "Why a client cannot choose its prize"). The
    /// model's own guards, not this pending draw, prevent a double payout. A restarted actor starts <see cref="Idle"/>
    /// without losing a draw, because the SDK persists the enqueued action with the model and runs it after the
    /// restart.
    /// </summary>
    public readonly struct PendingWheelDraw
    {
        /// <summary>
        /// How long the actor waits before sending an unresolved draw again. It limits how many copies of one
        /// action a client can cause by pressing repeatedly. Every copy resolves the same spin, so the interval
        /// only limits traffic and does not affect the result.
        /// </summary>
        public static readonly MetaDuration ResendInterval = MetaDuration.FromSeconds(10);

        /// <summary>
        /// The spin the draw was made for, or -1 when nothing has been drawn. A committed spin advances the
        /// player's ordinal, which releases the pending draw. It has no deadline on purpose: a failed action leaves
        /// the ordinal unchanged, and releasing it on a timer would allow a second draw for one spin.
        /// </summary>
        public int Ordinal { get; }

        /// <summary>The drawn sector, as a zero-based index into the active wheel table's sectors.</summary>
        public int SectorIndex { get; }

        /// <summary>The earliest time the same draw may be sent to the client again.</summary>
        public MetaTime ResendAfter { get; }

        PendingWheelDraw(int ordinal, int sectorIndex, MetaTime resendAfter)
        {
            Ordinal     = ordinal;
            SectorIndex = sectorIndex;
            ResendAfter = resendAfter;
        }

        /// <summary>Nothing drawn. The state of a newly started actor.</summary>
        public static readonly PendingWheelDraw Idle = new PendingWheelDraw(-1, -1, MetaTime.Epoch);

        /// <summary>
        /// Returns the pending draw for a sector drawn for <paramref name="ordinal"/> at <paramref name="now"/>.
        /// </summary>
        public static PendingWheelDraw Enqueued(int ordinal, int sectorIndex, MetaTime now) =>
            new PendingWheelDraw(ordinal, sectorIndex, now + ResendInterval);

        /// <summary>
        /// Whether a sector has already been drawn for <paramref name="ordinal"/>. While this is true the
        /// player's next request is answered with <see cref="SectorIndex"/> and the wheel is not drawn again.
        /// </summary>
        public bool HasDrawFor(int ordinal) => Ordinal >= 0 && Ordinal == ordinal;

        /// <summary>Whether the draw being held should be sent to the client again.</summary>
        public bool MayResend(MetaTime now) => now >= ResendAfter;

        /// <summary>Returns the pending draw after the same draw was sent again at <paramref name="now"/>.</summary>
        public PendingWheelDraw Resent(MetaTime now) => new PendingWheelDraw(Ordinal, SectorIndex, now + ResendInterval);

        public override string ToString() =>
            Ordinal < 0 ? "nothing drawn" : $"spin {Ordinal} drawn on sector {SectorIndex + 1}, re-sent after {ResendAfter}";
    }
}
