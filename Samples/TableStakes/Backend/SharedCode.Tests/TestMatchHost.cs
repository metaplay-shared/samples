using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// A host environment with no network, timers or entity. It executes each action the driver publishes directly
    /// against the model, which is the same effect a real host's timeline has on its model.
    /// <para>
    /// It records every published action, hand correction and seat loss, because most seat rules are only
    /// observable through what the driver published and which seats it told about a hand change.
    /// </para>
    /// </summary>
    public sealed class TestMatchHost : IMatchHostEnvironment
    {
        readonly MatchModel _model;
        readonly RandomPCG  _rng;

        /// <summary>Every action the driver published, in order.</summary>
        public readonly List<MatchAction> PublishedActions = new List<MatchAction>();

        /// <summary>Every seat the driver said needed a hand correction, in order, with repeats.</summary>
        public readonly List<int> HandCorrectedSeats = new List<int>();

        public TestMatchHost(MatchModel model, ulong seed = 12345UL)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _rng   = RandomPCG.CreateFromSeed(seed);
        }

        public bool Publish(MatchAction action)
        {
            MetaActionResult dryRun = action.InvokeExecute(_model, commit: false);
            if (!dryRun.IsSuccess)
                return false;

            action.InvokeExecute(_model, commit: true);
            PublishedActions.Add(action);
            return true;
        }

        public ulong NextSeed() => _rng.NextULong();

        public void OnSeatHandChanged(int seat) => HandCorrectedSeats.Add(seat);

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
    }

    /// <summary>An environment that refuses every publish.</summary>
    public sealed class RefusingMatchHost : IMatchHostEnvironment
    {
        public bool Publish(MatchAction action) => false;
        public ulong NextSeed() => 1UL;
        public void OnSeatHandChanged(int seat) { }
        public void OnSeatLost(int seat, MatchSeatLossReason reason) { }
    }

    /// <summary>
    /// An environment that forwards each publish to a delegate from the test. Use it when a test only cares about
    /// the published actions and not the rest of <see cref="IMatchHostEnvironment"/>.
    /// </summary>
    public sealed class DelegateMatchHost : IMatchHostEnvironment
    {
        readonly Func<MatchAction, bool> _publish;

        /// <summary>Every seat the driver said needed a hand correction.</summary>
        public readonly List<int> HandCorrectedSeats = new List<int>();

        public DelegateMatchHost(Func<MatchAction, bool> publish)
        {
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        public bool Publish(MatchAction action) => _publish(action);
        public ulong NextSeed() => 1UL;
        public void OnSeatHandChanged(int seat) => HandCorrectedSeats.Add(seat);
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
