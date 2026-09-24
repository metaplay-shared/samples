using Metaplay.Core;
using System;

namespace Game.Server
{
    /// <summary>
    /// A one-shot wake that an actor arms for an absolute timestamp (<c>docs/match.md</c>, "Timers"). The actor
    /// schedules the callback itself with the delay from <see cref="TryArm"/>.
    /// <para>
    /// Scheduled callbacks are never cancelled. Instead, the callback passes the timestamp it was scheduled for to
    /// <see cref="TryConsume"/>, which refuses it if the wake has since been cleared or armed for another time, so an
    /// outdated callback cannot act on a rescheduled deadline.
    /// </para>
    /// </summary>
    public sealed class DeadlineWakeup
    {
        /// <summary>
        /// Extra delay added after the timestamp. A callback that fires slightly early finds its deadline not yet
        /// reached and does nothing, and because nothing arms another wake, the actor would stall until some other
        /// message arrived.
        /// </summary>
        public static readonly TimeSpan Padding = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// The timestamp the currently scheduled callback is for, or <see cref="MetaTime.Epoch"/> when no wake is
        /// armed.
        /// </summary>
        MetaTime _armedAt = MetaTime.Epoch;

        /// <summary>
        /// Arms the wake for <paramref name="at"/>, or clears it when <paramref name="at"/> is
        /// <see cref="MetaTime.Epoch"/>. Returns true, with the delay to schedule the callback after, only when the
        /// caller must schedule a new callback. A wake already armed for the same time is left alone, because two
        /// callbacks for one time would both pass <see cref="TryConsume"/>.
        /// </summary>
        public bool TryArm(MetaTime at, MetaTime now, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;

            if (at == MetaTime.Epoch)
            {
                _armedAt = MetaTime.Epoch;
                return false;
            }

            if (_armedAt == at)
                return false;

            _armedAt = at;
            delay    = (at > now ? (at - now).ToTimeSpan() : TimeSpan.Zero) + Padding;
            return true;
        }

        /// <summary>
        /// Returns whether the callback scheduled for <paramref name="scheduledAt"/> is still the armed one, and if
        /// so disarms the wake so the callback runs once.
        /// </summary>
        public bool TryConsume(MetaTime scheduledAt)
        {
            if (_armedAt != scheduledAt)
                return false;

            _armedAt = MetaTime.Epoch;
            return true;
        }
    }
}
