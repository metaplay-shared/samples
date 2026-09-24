using Metaplay.Core;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// A dealt match as a model, built the way the actor builds one: <see cref="Deal.Create"/> and then the
    /// table's own public members. No host, no session, no network — which is the property the whole rules
    /// layer is meant to keep.
    /// </summary>
    public static class DealtModels
    {
        /// <summary> A fixed account id, so two models built for a comparison differ only where intended. </summary>
        public static readonly EntityId HumanSeatId = EntityId.Create(EntityKindCore.Player, 1234);

        public static MatchModel Build(SharedGameConfig config, ulong dealSeed, ulong botSeed, MatchTimings? timings = null)
        {
            MatchTimings resolved = timings ?? MatchTimings.Instant;

            MatchModel model = new MatchModel
            {
                GameConfig       = config,
                Timings          = resolved,
                Stakes           = MatchStakes.Practice(new List<int> { 25, 25 }, new List<List<CardId>> { new List<CardId>(), new List<CardId>() }),
                Phase            = MatchTablePhase.Playing,
                ResultAcked      = new List<bool> { false, false },
                HeistEligibility = new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() },
                History          = new List<MatchEvent>(),
                Seats = new List<MatchSeat>
                {
                    new MatchSeat(HumanSeatId, "Seat0", SeatOccupancy.Human, BotProfileId.Strongest),
                    new MatchSeat(EntityId.None, "Seat1", SeatOccupancy.Bot, BotProfileId.Practiced),
                },
            };

            Deal.Create(model, new MatchSetup(dealSeed, config, resolved, TestDecks.Standard(config), TestDecks.Alternate(config)), botSeed);
            return model;
        }

        /// <summary> The bytes the SDK would send a subscriber. </summary>
        public static byte[] Wire(MatchModel model)
            => MetaSerialization.SerializeTagged(model, MetaSerializationFlags.SendOverNetwork, logicVersion: null);

        /// <summary> The bytes the SDK would checksum. </summary>
        public static byte[] Checksummed(MatchModel model)
            => MetaSerialization.SerializeTagged(model, MetaSerializationFlags.ComputeChecksum, logicVersion: null);

        /// <summary>
        /// Everything, secret included. <c>IncludeAll</c> rather than <c>Persisted</c> on purpose: the two
        /// agree for this model (nothing on it is transient) and <c>IncludeAll</c> is the stronger of the
        /// two, so a comparison against it catches a member the persisted mask would have dropped. The name
        /// says what it is for — the half of a comparison that has to see the hidden state.
        /// </summary>
        public static byte[] Everything(MatchModel model)
            => MetaSerialization.SerializeTagged(model, MetaSerializationFlags.IncludeAll, logicVersion: null);

        /// <summary>
        /// The model as a follower holds it: through the SDK's own network mask, with the config resolver
        /// re-attached the way <c>DefaultDeserializeModel</c> attaches it.
        /// </summary>
        public static MatchModel AsFollower(MatchModel model, SharedGameConfig config)
        {
            MatchModel follower = MetaSerialization.DeserializeTagged<MatchModel>(
                Wire(model), MetaSerializationFlags.SendOverNetwork, resolver: config, logicVersion: null);
            follower.GameConfig = config;
            return follower;
        }
    }
}
