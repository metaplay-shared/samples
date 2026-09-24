using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Everything about this match that only the server may know. It hangs off exactly one
    /// <c>[ServerOnly]</c> member of <see cref="MatchModel"/>, so "the secret is present" is one null check
    /// and "nothing hidden reached the wire" is one member for the shape test to look at
    /// (<c>Docs/hidden-information.md</c>).
    /// <para>
    /// It is reached only through <see cref="MatchModel.SecretSeat"/> and read and written only by
    /// <see cref="SecretOps"/>. Never sent, never checksummed.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchSecrets
    {
        /// <summary>
        /// The match's single seeded stream. Its position is as secret as the seed: the stream replays
        /// forward in closed form (<c>Docs/hidden-information.md</c>, "The seed is part of the
        /// secret").
        /// </summary>
        [MetaMember(1)] public RandomPCG Rng { get; private set; }

        /// <summary>
        /// The bot seed, drawn independently of the deal seed from the same cryptographic source, and as
        /// secret. Bot policies are keyed on <em>this</em> and never on the deal seed, so a recovered
        /// substream key cannot name the deal (<c>Docs/hidden-information.md</c>).
        /// </summary>
        [MetaMember(2)] public ulong BotSeed { get; private set; }

        /// <summary> Exactly two, index == seat. </summary>
        [MetaMember(3)] public List<SeatSecrets> Seats { get; private set; }

        public MatchSecrets() { }

        public MatchSecrets(RandomPCG rng, ulong botSeed)
        {
            Rng     = rng;
            BotSeed = botSeed;
            Seats   = new List<SeatSecrets>(MatchSeats.Count);
            for (int seat = 0; seat < MatchSeats.Count; seat++)
                Seats.Add(new SeatSecrets());
        }
    }

    /// <summary> One seat's hidden half. Null on every client, including that seat's own. </summary>
    [MetaSerializable]
    public class SeatSecrets
    {
        /// <summary>
        /// Index 0 is the top. <b>Order</b> is the only thing about a deck that is secret; the contents are
        /// answered publicly by <see cref="SeatState.UnseenPool"/>.
        /// </summary>
        [MetaMember(1)] public List<CardInstanceId> Deck { get; private set; } = new List<CardInstanceId>();

        /// <summary> Contents secret, size public (<see cref="SeatState.HandCount"/>). </summary>
        [MetaMember(2)] public List<CardInstanceId> Hand { get; private set; } = new List<CardInstanceId>();

        /// <summary>
        /// What each of this seat's instances <em>is</em>, for the ones that have not become public. The
        /// public registry entry for one of these carries no card and no rank. Looked up by id and never
        /// iterated by a public mutation.
        /// </summary>
        [MetaMember(3)] public MetaDictionary<CardInstanceId, HiddenCard> Cards { get; private set; }
            = new MetaDictionary<CardInstanceId, HiddenCard>();

        // ---- a held peek, carried between the action that holds it and the one that answers it ----

        /// <summary>
        /// What a held peek revealed to this seat, top of deck first. The public half is
        /// <c>Rules.PendingChoice.RevealedCount</c>.
        /// </summary>
        [MetaMember(5)] public List<CardInstanceId> PeekRevealed { get; private set; }

        public SeatSecrets() { }

        internal void SetPeekRevealed(List<CardInstanceId> revealed) => PeekRevealed = revealed;
    }

    /// <summary>
    /// What a hidden instance is. The public entry learns this only when the card becomes public, at which
    /// point this is forgotten — one copy of the answer, never two that could disagree.
    /// </summary>
    [MetaSerializable]
    public readonly struct HiddenCard
    {
        [MetaMember(1)] public readonly MetaRef<CardInfo> Card;
        [MetaMember(2)] public readonly int               Rank;

        [MetaDeserializationConstructor]
        public HiddenCard(MetaRef<CardInfo> card, int rank)
        {
            Card = card;
            Rank = rank;
        }

        public CardInfo  Info   => Card.Ref;
        public CardId    CardId => Card.Ref.CardId;
        public CardStats Stats  => Card.Ref.GetStatsAtRank(Rank);
    }
}
