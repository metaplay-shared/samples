using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// One card in a seat's own hand: the instance, which card it is, and the rank its owner brought it at.
    /// <para>
    /// Deliberately <b>not</b> what it costs to play. A cost is derived from public state — the card, the rank,
    /// the Weather, how many tricks the seat has cast this turn — and a hand maintained by changes rather than
    /// resent whole would carry a stale one the moment the Weather turned. It is computed where it is shown,
    /// through <see cref="ManaRules.CostToPlay"/>, the same function the rules use.
    /// </para>
    /// <para>
    /// It exists because the one legality walk takes the acting seat's hand as an explicit argument and the
    /// two callers keep it in different places — the server builds these out of
    /// <see cref="SeatSecrets.Cards"/>, a client reads them off the hand the private channel delivered — and
    /// neither may reach into the other's. Everything else the walk reads is public and is on the model, so
    /// the two callers cannot disagree about it without the session ending.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public readonly struct HandCard
    {
        [MetaMember(1)] public readonly CardInstanceId Instance;
        [MetaMember(2)] public readonly CardId         Card;
        [MetaMember(3)] public readonly int            Rank;

        [MetaDeserializationConstructor]
        public HandCard(CardInstanceId instance, CardId card, int rank)
        {
            Instance = instance;
            Card     = card;
            Rank     = rank;
        }

        public override string ToString() => $"{Instance} {Card} r{Rank}";
    }
}
