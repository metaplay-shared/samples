using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Globalization;

namespace Game.Logic
{
    /// <summary>
    /// Links the separate analytics events that one action causes, such as a reward claim and the currency
    /// changes it made. Events with the same cause carry the same ID (<c>docs/analytics.md</c>, "Correlation id").
    /// <para>
    /// The ID is a join key, not a unique identifier. It is computed from its inputs instead of being random, so
    /// shared code can create one without a random source. It is only meaningful within one player's event log:
    /// two players can get the same value.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public struct AnalyticsCorrelationId : IEquatable<AnalyticsCorrelationId>
    {
        /// <summary>
        /// The 64-bit key, stored as a signed value because the analytics export writes integer fields as
        /// BigQuery INT64 and rejects unsigned 64-bit fields. All 64 bits are kept, so events with the same cause
        /// still have equal values.
        /// </summary>
        [MetaMember(1)] public long Value { get; private set; }

        /// <summary>The value for an event that has no related events.</summary>
        public static readonly AnalyticsCorrelationId None = default;

        public AnalyticsCorrelationId(long value)
        {
            Value = value;
        }

        public bool IsSet => Value != 0;

        /// <summary>
        /// Returns the ID for one cause. The same subject, time and cause always give the same ID. Different
        /// inputs give different IDs with high probability, and the result is never <see cref="None"/>.
        /// </summary>
        /// <param name="subject">The entity whose event log the events are written to.</param>
        /// <param name="at">When the cause happened.</param>
        /// <param name="cause">What the cause was: a fixed literal naming the feature, or a stable ID of the thing
        /// that caused it, such as an offer ID or a purchase transaction ID. Never text a player entered.</param>
        public static AnalyticsCorrelationId Create(EntityId subject, MetaTime at, string cause)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause));

            // Mix the three inputs with the splitmix64 finalizer. Changing any input bit changes about half of
            // the output bits, so two causes a second apart do not collide. The ID does not need to be
            // unguessable.
            ulong mixed = subject.Value ^ ((ulong)subject.Kind.Value << 58);
            mixed = Mix(mixed ^ (ulong)at.MillisecondsSinceEpoch);
            mixed = Mix(mixed ^ Fnv1a(cause));

            // Zero means None, so a zero result is replaced with 1. The unchecked cast reinterprets the bits
            // without losing any.
            return new AnalyticsCorrelationId(mixed == 0 ? 1L : unchecked((long)mixed));
        }

        static ulong Mix(ulong value)
        {
            unchecked
            {
                value += 0x9E3779B97F4A7C15UL;
                ulong z = value;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        static ulong Fnv1a(string text)
        {
            unchecked
            {
                ulong hash = 0xCBF29CE484222325UL;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 0x100000001B3UL;
                }
                return hash;
            }
        }

        public bool Equals(AnalyticsCorrelationId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is AnalyticsCorrelationId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(AnalyticsCorrelationId a, AnalyticsCorrelationId b) => a.Equals(b);

        public static bool operator !=(AnalyticsCorrelationId a, AnalyticsCorrelationId b) => !a.Equals(b);

        /// <summary>Formats the value as 16 lower-case hex digits, so IDs can be compared by eye.</summary>
        public override string ToString() => Value.ToString("x16", CultureInfo.InvariantCulture);
    }
}
