using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Globalization;

namespace Game.Logic
{
    /// <summary>
    /// The value a step resolves for: a literal, a count of something public, or a literal plus a count.
    /// Authored as a small string grammar in the <c>EffectSteps</c> sheet and parsed into this typed form at
    /// config-build time, so a malformed cell is a build failure rather than a runtime surprise.
    /// <code>
    /// amount  := literal | per | literal '+' per
    /// literal := '-'? digit+
    /// per     := (digit+ 'x')? 'Per:' counter
    /// </code>
    /// The value at resolution is <c>Literal + PerUnit * count(Counter)</c>, evaluated when the step is
    /// dequeued. The per-unit factor exists because the stat domain is coarser than one: a counter that
    /// contributed a bare 1 per unit would be a rounding error against a Den measured in hundreds, so a
    /// scaling card says how much each unit is worth (<c>5xPer:FriendlyCritters</c>) instead of relying on
    /// the count alone.
    /// </summary>
    [MetaSerializable]
    public class EffectAmount
    {
        /// <summary> The constant part. Negative only on <see cref="EffectOp.Buff"/> steps. </summary>
        [MetaMember(1)] public int Literal { get; private set; }

        /// <summary> The public quantity added to <see cref="Literal"/>, or none. </summary>
        [MetaMember(2)] public EffectCounter Counter { get; private set; }

        /// <summary>
        /// What one unit of <see cref="Counter"/> is worth. One unless the cell said otherwise, and zero
        /// when there is no counter at all, so that a bare literal cannot accidentally scale anything.
        /// </summary>
        [MetaMember(3)] public int PerUnit { get; private set; }

        public EffectAmount() { }

        public EffectAmount(int literal, EffectCounter counter = EffectCounter.None, int perUnit = 1)
        {
            Literal = literal;
            Counter = counter;
            PerUnit = counter == EffectCounter.None ? 0 : perUnit;
        }

        /// <summary> True when the amount is a plain number, i.e. knowable without reading the board. </summary>
        public bool IsLiteralOnly => Counter == EffectCounter.None;

        /// <summary>
        /// What this amount comes to, given a reading of its counter and whatever a rank track added to the
        /// literal. <b>Both</b> evaluators call this — the engine's, which resolves the step, and the bot's,
        /// which scores it — rather than repeating the arithmetic, because a policy that valued a scaling
        /// card differently from the table would be playing against numbers the table does not use. The two
        /// still read <paramref name="count"/> from their own state; what they cannot do any more is disagree
        /// about how the parts combine.
        /// </summary>
        public int ValueFrom(int count, int rankBonus = 0) => Literal + rankBonus + PerUnit * count;

        public override string ToString()
        {
            if (Counter == EffectCounter.None)
                return Literal.ToString(CultureInfo.InvariantCulture);

            string per = PerUnit == 1
                ? $"Per:{Counter}"
                : $"{PerUnit.ToString(CultureInfo.InvariantCulture)}xPer:{Counter}";

            if (Literal == 0)
                return per;
            return $"{Literal.ToString(CultureInfo.InvariantCulture)}+{per}";
        }

        const string PerPrefix = "Per:";

        /// <summary>
        /// Parse an authored amount cell. Returns false with a designer-readable <paramref name="error"/>
        /// rather than throwing, so the caller can attribute the failure to a sheet row.
        /// </summary>
        public static bool TryParse(string text, out EffectAmount amount, out string error)
        {
            amount = null;
            error  = null;

            string trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                error = "amount is empty";
                return false;
            }

            string literalPart = null;
            string perPart     = null;

            int plusNdx = trimmed.IndexOf('+');
            if (plusNdx >= 0)
            {
                literalPart = trimmed.Substring(0, plusNdx).Trim();
                perPart     = trimmed.Substring(plusNdx + 1).Trim();
            }
            else if (trimmed.IndexOf(PerPrefix, StringComparison.Ordinal) >= 0)
            {
                // Anywhere in the cell rather than at the front, because an optional per-unit factor sits
                // ahead of the term. A bare literal never contains the prefix, so this cannot claim one.
                perPart = trimmed;
            }
            else
                literalPart = trimmed;

            int literal = 0;
            if (literalPart != null && !int.TryParse(literalPart, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out literal))
            {
                error = $"'{literalPart}' is not an integer literal";
                return false;
            }

            EffectCounter counter = EffectCounter.None;
            int           perUnit = 1;
            if (perPart != null)
            {
                // An optional '<n>x' in front says what one unit is worth; without it a unit is worth one.
                int xNdx = perPart.IndexOf('x');
                if (xNdx > 0)
                {
                    string unitPart = perPart.Substring(0, xNdx).Trim();
                    if (!int.TryParse(unitPart, NumberStyles.None, CultureInfo.InvariantCulture, out perUnit) || perUnit < 1)
                    {
                        error = $"'{unitPart}' is not a positive per-unit factor";
                        return false;
                    }
                    perPart = perPart.Substring(xNdx + 1).Trim();
                }

                if (!perPart.StartsWith(PerPrefix, StringComparison.Ordinal))
                {
                    error = $"'{perPart}' is not a 'Per:<counter>' term";
                    return false;
                }

                string counterName = perPart.Substring(PerPrefix.Length).Trim();
                if (!EffectVocabulary.TryParseName(counterName, out counter) || counter == EffectCounter.None)
                {
                    error = $"'{counterName}' is not a known counter";
                    return false;
                }
            }

            amount = new EffectAmount(literal, counter, perUnit);
            return true;
        }
    }

    /// <summary>
    /// Constrains what a <c>Per:</c> counter counts or what a graveyard selection considers. An empty filter
    /// admits everything; at most one clause of each kind is allowed.
    /// <code>
    /// filter := clause (';' clause)*
    /// clause := 'Clan=' &lt;ClanId&gt; | 'Type=' ('Critter' | 'Trick')
    /// </code>
    /// </summary>
    [MetaSerializable]
    public class EffectFilter
    {
        /// <summary> The clan a card must belong to, or null for any clan. </summary>
        [MetaMember(1)] public MetaRef<ClanInfo> Clan { get; private set; }

        /// <summary> The card type a card must be, or <see cref="CardTypeFilter.Any"/>. </summary>
        [MetaMember(2)] public CardTypeFilter Type { get; private set; }

        public EffectFilter() { }

        public EffectFilter(MetaRef<ClanInfo> clan, CardTypeFilter type)
        {
            Clan = clan;
            Type = type;
        }

        /// <summary> Whether a card passes both clauses. Requires the clan reference to be resolved. </summary>
        public bool Matches(CardInfo card)
        {
            if (Clan != null && !Clan.Ref.ClanId.Equals(card.Clan.Ref.ClanId))
                return false;

            switch (Type)
            {
                case CardTypeFilter.Critter: return card.Type == CardType.Critter;
                case CardTypeFilter.Trick:   return card.Type == CardType.Trick;
                default:                     return true;
            }
        }

        public override string ToString()
        {
            string clanClause = Clan != null ? $"Clan={Clan.KeyObject}" : null;
            string typeClause = Type != CardTypeFilter.Any ? $"Type={Type}" : null;
            if (clanClause != null && typeClause != null)
                return $"{clanClause};{typeClause}";
            return clanClause ?? typeClause ?? "";
        }

        /// <summary>
        /// Parse an authored filter cell into a filter whose clan reference is unresolved — the config build
        /// resolves it with every other reference, so a filter naming an unknown clan fails the build.
        /// </summary>
        public static bool TryParse(string text, out EffectFilter filter, out string error)
        {
            filter = null;
            error  = null;

            string trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                error = "filter is empty";
                return false;
            }

            MetaRef<ClanInfo> clan = null;
            CardTypeFilter    type = CardTypeFilter.Any;

            foreach (string rawClause in trimmed.Split(';'))
            {
                string clause = rawClause.Trim();
                if (clause.Length == 0)
                {
                    error = "filter has an empty clause";
                    return false;
                }

                int eqNdx = clause.IndexOf('=');
                if (eqNdx <= 0 || eqNdx == clause.Length - 1)
                {
                    error = $"'{clause}' is not a '<kind>=<value>' clause";
                    return false;
                }

                string kind  = clause.Substring(0, eqNdx).Trim();
                string value = clause.Substring(eqNdx + 1).Trim();

                if (kind == "Clan")
                {
                    if (clan != null)
                    {
                        error = "filter has more than one Clan clause";
                        return false;
                    }
                    clan = MetaRef<ClanInfo>.FromKey(ClanId.FromString(value));
                }
                else if (kind == "Type")
                {
                    if (type != CardTypeFilter.Any)
                    {
                        error = "filter has more than one Type clause";
                        return false;
                    }
                    if (!EffectVocabulary.TryParseName(value, out type) || type == CardTypeFilter.Any)
                    {
                        error = $"'{value}' is not a card type";
                        return false;
                    }
                }
                else
                {
                    error = $"'{kind}' is not a filter clause kind";
                    return false;
                }
            }

            filter = new EffectFilter(clan, type);
            return true;
        }
    }
}
