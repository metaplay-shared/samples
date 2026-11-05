using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// A host environment with no network, timers or entity. It executes each action the driver publishes directly
    /// against the model, which is the same effect a real host's timeline has on its model.
    /// <para>
    /// It records every published action and seat loss, because most seat rules are only observable through what
    /// the driver published.
    /// </para>
    /// <para>
    /// It also replays every published action on a client's copy of the model, taken over the wire at the first
    /// publish, and throws when the copy's checksum differs from the host's. So every test that drives a table
    /// through this host also checks that no action reads server-only state.
    /// </para>
    /// </summary>
    public sealed class TestMatchHost : IMatchHostEnvironment
    {
        readonly MatchModel _model;
        readonly RandomPCG  _rng;
        readonly bool       _followOnAClientCopy;

        /// <summary>A client's copy of the model, created at the first publish.</summary>
        MatchModel _follower;

        /// <summary>Every action the driver published, in order.</summary>
        public readonly List<MatchAction> PublishedActions = new List<MatchAction>();

        /// <summary>Every action type this host replayed on a client's copy.</summary>
        public readonly HashSet<Type> FollowedActionTypes = new HashSet<Type>();

        /// <param name="followOnAClientCopy">
        /// Whether to replay each action on a client's copy. Only the render fixtures in <c>WebClient.Tests</c> turn it
        /// off, because they build tables in a process without the SDK's serializer.
        /// </param>
        public TestMatchHost(MatchModel model, ulong seed = 12345UL, bool followOnAClientCopy = true)
        {
            _model               = model ?? throw new ArgumentNullException(nameof(model));
            _rng                 = RandomPCG.CreateFromSeed(seed);
            _followOnAClientCopy = followOnAClientCopy;
        }

        public bool Publish(MatchAction action)
        {
            MetaActionResult dryRun = action.InvokeExecute(_model, commit: false);
            if (!dryRun.IsSuccess)
                return false;

            MatchModel follower = _followOnAClientCopy ? (_follower ??= OverTheWire(_model)) : null;

            action.InvokeExecute(_model, commit: true);
            PublishedActions.Add(action);

            if (follower != null)
            {
                MetaActionResult followed = action.InvokeExecute(follower, commit: true);
                if (!followed.IsSuccess)
                    throw new InvalidOperationException($"{action.GetType().Name} succeeded on the host but was refused on a client's copy: {followed}");
                if (!Checksummed(follower).AsSpan().SequenceEqual(Checksummed(_model)))
                    throw new InvalidOperationException($"{action.GetType().Name} left a client's copy with a different checksum than the host. The action read or wrote server-only state.");

                FollowedActionTypes.Add(action.GetType());
            }
            return true;
        }

        public ulong NextSeed() => _rng.NextULong();

        /// <summary>Every seat loss the driver reported, in order, with the reason it gave.</summary>
        public readonly List<(int Seat, MatchSeatLossReason Reason)> SeatLosses = new List<(int, MatchSeatLossReason)>();

        public void OnSeatLost(int seat, MatchSeatLossReason reason) => SeatLosses.Add((seat, reason));

        /// <summary>How many actions of one kind were published.</summary>
        public int CountOf<T>() where T : MatchAction
        {
            int count = 0;
            foreach (MatchAction action in PublishedActions)
            {
                if (action is T)
                    count++;
            }
            return count;
        }

        /// <summary>Returns a client's copy of <paramref name="model"/>: serialized and deserialized with <see cref="MetaSerializationFlags.SendOverNetwork"/>.</summary>
        public static MatchModel OverTheWire(MatchModel model)
        {
            byte[] bytes = MetaSerialization.SerializeTagged<IMultiplayerModel>(model, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            return (MatchModel)MetaSerialization.DeserializeTagged<IMultiplayerModel>(bytes, MetaSerializationFlags.SendOverNetwork, resolver: null, logicVersion: null);
        }

        static byte[] Checksummed(MatchModel model)
            => MetaSerialization.SerializeTagged(model, MetaSerializationFlags.ComputeChecksum, logicVersion: null);
    }

    /// <summary>An environment that refuses every publish.</summary>
    public sealed class RefusingMatchHost : IMatchHostEnvironment
    {
        public bool Publish(MatchAction action) => false;
        public ulong NextSeed() => 1UL;
        public void OnSeatLost(int seat, MatchSeatLossReason reason) { }
    }

    /// <summary>
    /// An environment that forwards each publish to a delegate from the test. Use it when a test only cares about
    /// the published actions and not the rest of <see cref="IMatchHostEnvironment"/>.
    /// </summary>
    public sealed class DelegateMatchHost : IMatchHostEnvironment
    {
        readonly Func<MatchAction, bool> _publish;

        public DelegateMatchHost(Func<MatchAction, bool> publish)
        {
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        public bool Publish(MatchAction action) => _publish(action);
        public ulong NextSeed() => 1UL;
        public void OnSeatLost(int seat, MatchSeatLossReason reason) { }
    }

    /// <summary>
    /// Shorthands for creating a <see cref="DelegateMatchHost"/> or a <see cref="RefusingMatchHost"/>.
    /// </summary>
    public static class TestMatchHostEnvironments
    {
        public static IMatchHostEnvironment Publishing(Func<MatchAction, bool> publish) => new DelegateMatchHost(publish);

        public static IMatchHostEnvironment Refusing => new RefusingMatchHost();
    }
}
