namespace Game.Logic
{
    /// <summary>
    /// What an instance is, from wherever that is knowable. A public instance answers from the registry,
    /// which every follower has; a hidden one answers from its owner's secret, which only the server has.
    /// <para>
    /// A follower that asks about a hidden card gets <c>null</c>, and that is what makes "no public mutation
    /// depends on a hidden identity" <em>checkable</em>: the null would have to reach a public write for a
    /// divergence to happen, and the follower harness runs every action of every game looking for exactly
    /// that.
    /// </para>
    /// </summary>
    public static class CardLookup
    {
        /// <summary> The catalogue card an instance is, or null when this side cannot know. </summary>
        public static CardInfo Info(MatchModel match, CardInstanceId id)
        {
            CardInstance instance = match.Rules.TryGetInstance(id);
            if (instance == null)
                return null;

            if (instance.IsKnown)
                return instance.Info;

            HiddenCard? hidden = SecretOps.HiddenCardOf(match, id);
            return hidden?.Info;
        }

        /// <summary> The rank an instance is held at, or zero when this side cannot know. </summary>
        public static int Rank(MatchModel match, CardInstanceId id)
        {
            CardInstance instance = match.Rules.TryGetInstance(id);
            if (instance == null)
                return 0;

            if (instance.IsKnown)
                return instance.Rank;

            HiddenCard? hidden = SecretOps.HiddenCardOf(match, id);
            return hidden?.Rank ?? 0;
        }

        /// <summary> Which card an instance is, or null when this side cannot know. </summary>
        public static CardId CardId(MatchModel match, CardInstanceId id) => Info(match, id)?.CardId;
    }
}
