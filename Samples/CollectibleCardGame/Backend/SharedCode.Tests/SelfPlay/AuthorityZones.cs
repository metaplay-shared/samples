namespace Game.Logic.Tests
{
    /// <summary>
    /// Where a card actually is, as the <em>authority</em> knows it. Five values, because the game has five
    /// zones — which is one more distinction than the public registry makes, and deliberately so: a public
    /// entry says <see cref="CardPlace.Unseen"/> for both hidden zones, because saying which of the two would
    /// let a client subtract the hand out of the unseen pool.
    /// </summary>
    public enum AuthorityZone
    {
        Deck      = 0,
        Hand      = 1,
        Board     = 2,
        Graveyard = 3,
        /// <summary> Minted but not yet placed: a token or a copy between its creation and its first zone. </summary>
        Limbo     = 4,
    }

    /// <summary>
    /// The zone question the invariant catalog and the rules suites ask. It is a <b>test-side</b> notion on
    /// purpose: answering it needs the secret, so no client could ask it, and production never does — the
    /// rules branch on public counts and on <see cref="CardInstance.Place"/>.
    /// <para>
    /// It is what the old <c>CardZone</c> was, reassembled from the two halves the refactor split it into:
    /// the seat's secret lists for the hidden zones, the public entry for the rest.
    /// </para>
    /// </summary>
    public static class AuthorityZones
    {
        public static AuthorityZone Of(MatchModel match, CardInstanceId id)
        {
            CardInstance instance = match.Rules.TryGetInstance(id);
            if (instance == null)
                return AuthorityZone.Limbo;

            switch (instance.Place)
            {
                case CardPlace.Board:     return AuthorityZone.Board;
                case CardPlace.Graveyard: return AuthorityZone.Graveyard;
            }

            SeatSecrets secret = match.SecretSeat(instance.Owner);
            if (secret != null)
            {
                if (secret.Hand.Contains(id))
                    return AuthorityZone.Hand;
                if (secret.Deck.Contains(id))
                    return AuthorityZone.Deck;
            }

            // A public card that is in a hand or a deck says so on its own entry; anything else is in flight.
            switch (instance.Place)
            {
                case CardPlace.Hand: return AuthorityZone.Hand;
                case CardPlace.Deck: return AuthorityZone.Deck;
                default:             return AuthorityZone.Limbo;
            }
        }

        public static AuthorityZone Of(MatchModel match, CardInstance instance) => Of(match, instance.Id);
    }
}
