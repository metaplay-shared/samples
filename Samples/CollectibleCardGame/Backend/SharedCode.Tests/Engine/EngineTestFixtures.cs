using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Deck lists for engine tests. Real cards from the checked-in archive, because a rules engine tested
    /// against invented content proves nothing about the content that ships.
    /// </summary>
    public static class TestDecks
    {
        /// <summary> A legal 25-card list: the Wanderers plus two clans, in canonical card order. </summary>
        public static List<MatchDeckCard> Standard(SharedGameConfig config, int rank = 1)
            => Build(config, rank, "Wanderer", "Kitsune", "Tidepool");

        /// <summary> A second legal list drawing on the other clans, so the two seats are not mirror images. </summary>
        public static List<MatchDeckCard> Alternate(SharedGameConfig config, int rank = 1)
            => Build(config, rank, "Wanderer", "Sunny", "Moonlight");

        static List<MatchDeckCard> Build(SharedGameConfig config, int rank, params string[] clanOrder)
        {
            List<MatchDeckCard> deck = new List<MatchDeckCard>();

            foreach (string clan in clanOrder)
            {
                List<CardInfo> inClan = new List<CardInfo>();
                foreach (CardInfo card in config.Cards.Values)
                {
                    if (card.Collectible && card.Clan.Ref.ClanId.Value == clan)
                        inClan.Add(card);
                }

                inClan.Sort((a, b) => CardInfo.CompareCanonical(a.CardId, b.CardId));

                foreach (CardInfo card in inClan)
                {
                    if (deck.Count >= config.Global.DeckSize)
                        break;
                    deck.Add(new MatchDeckCard(card.CardId, rank));
                }
            }

            if (deck.Count != config.Global.DeckSize)
                throw new InvalidOperationException($"Test deck built {deck.Count} cards, expected {config.Global.DeckSize}");

            return deck;
        }

        /// <summary> A deck whose top cards are named and whose tail is filled with the standard list. </summary>
        public static List<MatchDeckCard> Named(SharedGameConfig config, params string[] cardIds)
        {
            List<MatchDeckCard> deck = new List<MatchDeckCard>();
            HashSet<string>     used = new HashSet<string>();

            foreach (string id in cardIds)
            {
                deck.Add(new MatchDeckCard(CardId.FromString(id), 1));
                used.Add(id);
            }

            foreach (MatchDeckCard filler in Standard(config))
            {
                if (deck.Count >= config.Global.DeckSize)
                    break;
                if (used.Add(filler.Card.Value))
                    deck.Add(filler);
            }

            return deck;
        }
    }

    /// <summary>
    /// The bot policy over the test content. Engine suites that need "somebody to play this seat" use the real
    /// policy rather than a stand-in, which is what makes them exercise the code the match actor will call.
    /// </summary>
    public static class TestBots
    {
        /// <summary> The profile cover, auto-play and practice always use. Stateless, so one instance serves everything. </summary>
        public static BotPolicy Strongest => _strongest.Value;

        static readonly Lazy<BotPolicy> _strongest =
            new Lazy<BotPolicy>(() => BotPolicy.Strongest(TestGameConfig.Shared), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Builds a game that is already in progress. Combat and keyword rules are about board states that a deal
    /// would take twenty turns to reach, so those tests construct the model and wrap it rather than playing
    /// their way there.
    /// <para>
    /// It assembles the <em>secret</em> as well as the public half, because a hand and a deck live there now:
    /// <see cref="SeatBuilder.Hand"/> and <see cref="SeatBuilder.Deck"/> push into the seat's secret lists,
    /// bump the stored public counts, and mint a public registry entry that says nothing but "unseen".
    /// </para>
    /// </summary>
    public sealed class Scenario
    {
        readonly SharedGameConfig   _config;
        readonly List<CardInstance> _instances = new List<CardInstance>();
        readonly SeatBuilder[]      _builders  = new SeatBuilder[MatchSeats.Count];

        MetaRef<WeatherInfo> _weather;
        MatchPhase           _phase      = MatchPhase.Playing;
        int                  _turn       = 1;
        int                  _firstSeat;
        int                  _seatOnTurn;
        ulong                _seed       = 1;

        public Scenario(SharedGameConfig config, string weatherId = null)
        {
            _config = config;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
                _builders[seat] = new SeatBuilder(this, seat, config.Global.DenStartingHp);

            if (weatherId != null)
                _weather = MetaRef<WeatherInfo>.FromItem(config.Weathers[WeatherId.FromString(weatherId)]);
        }

        public SharedGameConfig Config => _config;

        public SeatBuilder Seat(int seat) => _builders[seat];

        /// <summary>
        /// A Weather given as a row rather than by id, so a hook the engine reads stays testable
        /// independently of what the content sheet happens to hold. Built the way
        /// <c>ContentValidatorTests</c> builds a synthetic pool: a real <see cref="WeatherInfo"/>, with real
        /// keyword and step references, that no shipped pool contains. Null is no Weather.
        /// </summary>
        public Scenario Weather(WeatherInfo weather)
        {
            _weather = weather == null ? null : MetaRef<WeatherInfo>.FromItem(weather);
            return this;
        }

        public Scenario OnTurn(int seat)
        {
            _seatOnTurn = seat;
            return this;
        }

        public Scenario FirstSeat(int seat)
        {
            _firstSeat = seat;
            return this;
        }

        public Scenario Turn(int turn)
        {
            _turn = turn;
            return this;
        }

        public Scenario Seed(ulong seed)
        {
            _seed = seed;
            return this;
        }

        public Scenario Phase(MatchPhase phase)
        {
            _phase = phase;
            return this;
        }

        /// <summary>
        /// Mint one instance. A public one gets its identity on the registry entry; a hidden one gets a blank
        /// entry at <see cref="CardPlace.Unseen"/> and its identity is remembered against the model's secret
        /// once <see cref="Build"/> has one to remember it in.
        /// </summary>
        internal CardInstance Mint(string cardId, int rank, int owner, CardPlace place, bool isPublic)
        {
            CardInfo       card = _config.Cards[CardId.FromString(cardId)];
            CardInstanceId id   = new CardInstanceId(_instances.Count);
            CardInstance   made = isPublic
                ? new CardInstance(id, MetaRef<CardInfo>.FromItem(card), rank, owner, place, isPublic: true, fromStartingDeck: true)
                : new CardInstance(id, owner, place, isPublic: false, fromStartingDeck: true);

            if (!isPublic)
                _hidden.Add(new HiddenMint(id, owner, MetaRef<CardInfo>.FromItem(card), rank));

            _instances.Add(made);
            return made;
        }

        /// <summary> A hidden instance's identity, held until the model's secret exists to hold it. </summary>
        readonly struct HiddenMint
        {
            public readonly CardInstanceId    Id;
            public readonly int               Owner;
            public readonly MetaRef<CardInfo> Card;
            public readonly int               Rank;

            public HiddenMint(CardInstanceId id, int owner, MetaRef<CardInfo> card, int rank)
            {
                Id    = id;
                Owner = owner;
                Card  = card;
                Rank  = rank;
            }
        }

        readonly List<HiddenMint> _hidden = new List<HiddenMint>();

        public MatchEngine Build(MatchTimings timings = default)
        {
            List<SeatState> seats = new List<SeatState>(MatchSeats.Count);
            for (int seat = 0; seat < MatchSeats.Count; seat++)
                seats.Add(_builders[seat].ToSeatState());

            MatchModel model = new MatchModel
            {
                GameConfig       = _config,
                Timings          = timings,
                Secret           = new MatchSecrets(RandomPCG.CreateFromSeed(_seed), botSeed: 0),
                Phase            = MatchTablePhase.Playing,
                ResultAcked      = new List<bool> { false, false },
                HeistEligibility = new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() },
                History          = new List<MatchEvent>(),
                Seats            = new List<MatchSeat>
                {
                    new MatchSeat(EntityId.None, "Seat0", SeatOccupancy.Bot, BotProfileId.Strongest),
                    new MatchSeat(EntityId.None, "Seat1", SeatOccupancy.Bot, BotProfileId.Strongest),
                },
                Rules = new MatchRulesState(
                    _weather,
                    _phase,
                    _turn,
                    _firstSeat,
                    _seatOnTurn,
                    seats,
                    _instances),
            };

            // The hidden identities, and the hidden zones the seat builders collected.
            foreach (HiddenMint mint in _hidden)
                SecretOps.RememberHiddenIdentity(model, mint.Owner, mint.Id, mint.Card, mint.Rank);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
                _builders[seat].FillSecret(model);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
                seats[seat].UnseenPool.AddRange(SecretDerivations.DeriveUnseenPool(model, seat));

            return MatchEngine.Wrap(model);
        }
    }

    /// <summary> One seat's half of a <see cref="Scenario"/>. </summary>
    public sealed class SeatBuilder
    {
        readonly Scenario             _scenario;
        readonly int                  _seat;
        readonly List<CardInstanceId> _deck      = new List<CardInstanceId>();
        readonly List<CardInstanceId> _hand      = new List<CardInstanceId>();
        readonly List<BoardCritter>   _board     = new List<BoardCritter>();
        readonly List<CardInstanceId> _graveyard = new List<CardInstanceId>();
        readonly List<CardInstanceId>    _publicHand = new List<CardInstanceId>();
        readonly List<CardId>            _played     = new List<CardId>();
        readonly List<HeistEligibleCard> _ownDeck    = new List<HeistEligibleCard>();

        int _den;
        int _mana    = 10;
        int _maxMana = 10;
        int _ticks;
        int _tricks;

        internal SeatBuilder(Scenario scenario, int seat, int denStartingHp)
        {
            _scenario = scenario;
            _seat     = seat;
            _den      = denStartingHp;
        }

        internal SeatState ToSeatState()
        {
            SeatState state = new SeatState(_den, _mana, _maxMana, _ticks, hasMulliganed: true, tricksCastThisTurn: _tricks);
            state.Board.AddRange(_board);
            state.Graveyard.AddRange(_graveyard);
            state.PlayedThisMatch.AddRange(_played);
            state.PlayedFromOwnDeck.AddRange(_ownDeck);

            // The two hidden zones are counts here; the lists themselves go into the secret at Build time.
            state.SetCounts(_hand.Count + _publicHand.Count, _deck.Count);
            return state;
        }

        /// <summary> Put this seat's hidden zones into the model's secret. </summary>
        internal void FillSecret(MatchModel model)
        {
            foreach (CardInstanceId id in _deck)
                SecretOps.AppendToDeck(model, _seat, id);

            // A hand holds hidden and public cards alike, and the secret list is the whole of it: the hand
            // views and the legal set are built off it, and a public card in a hand is still in that hand.
            foreach (CardInstanceId id in _hand)
                SecretOps.AppendToHand(model, _seat, id);
            foreach (CardInstanceId id in _publicHand)
                SecretOps.AppendToHand(model, _seat, id);
        }

        public SeatBuilder Den(int hp)
        {
            _den = hp;
            return this;
        }

        public SeatBuilder Mana(int mana, int maxMana = -1)
        {
            _mana    = mana;
            _maxMana = maxMana < 0 ? mana : maxMana;
            return this;
        }

        public SeatBuilder Tuckered(int ticks)
        {
            _ticks = ticks;
            return this;
        }

        public SeatBuilder Tricks(int cast)
        {
            _tricks = cast;
            return this;
        }

        /// <summary> Put a critter in play, optionally overriding what its card says it is. </summary>
        public CardInstanceId Board(
            string cardId,
            int rank = 1,
            int? attack = null,
            int? maxHealth = null,
            int damage = 0,
            KeywordFlags? keywords = null,
            bool sleepy = false,
            bool hasAttacked = false,
            bool? bubbleIntact = null)
        {
            CardInstance instance = _scenario.Mint(cardId, rank, _seat, CardPlace.Board, isPublic: true);
            CardStats    stats    = instance.Stats;
            KeywordFlags flags    = keywords ?? instance.Info.GetKeywordFlags();

            _board.Add(new BoardCritter(
                instance.Id,
                attack ?? stats.Attack,
                maxHealth ?? stats.Health,
                damage,
                flags,
                sleepy,
                hasAttacked,
                bubbleIntact ?? (flags & KeywordFlags.Bubble) != 0));

            return instance.Id;
        }

        /// <summary> Put a card in the hand, hidden — which is what a dealt or drawn card is. </summary>
        public CardInstanceId Hand(string cardId, int rank = 1)
        {
            CardInstance instance = _scenario.Mint(cardId, rank, _seat, CardPlace.Unseen, isPublic: false);
            _hand.Add(instance.Id);
            return instance.Id;
        }

        /// <summary>
        /// Put a card in the hand <em>publicly</em> — The Acorn, a bounced critter, a graveyard copy. Its
        /// registry entry names the card, so both sides can see it and it is outside the unseen pool.
        /// </summary>
        public CardInstanceId PublicHand(string cardId, int rank = 1)
        {
            CardInstance instance = _scenario.Mint(cardId, rank, _seat, CardPlace.Hand, isPublic: true);
            _publicHand.Add(instance.Id);
            return instance.Id;
        }

        /// <summary> Push a card onto the bottom of the deck. The first call is the top card. </summary>
        public CardInstanceId Deck(string cardId, int rank = 1)
        {
            CardInstance instance = _scenario.Mint(cardId, rank, _seat, CardPlace.Unseen, isPublic: false);
            _deck.Add(instance.Id);
            return instance.Id;
        }

        public SeatBuilder DeckOf(int count, string cardId = "MeadowMouse")
        {
            for (int ndx = 0; ndx < count; ndx++)
                Deck(cardId);
            return this;
        }

        public CardInstanceId Graveyard(string cardId, int rank = 1)
        {
            CardInstance instance = _scenario.Mint(cardId, rank, _seat, CardPlace.Graveyard, isPublic: true);
            _graveyard.Add(instance.Id);
            return instance.Id;
        }

        public SeatBuilder Played(string cardId, bool fromOwnDeck = true, int rank = 1)
        {
            CardId id = CardId.FromString(cardId);
            _played.Add(id);
            if (fromOwnDeck)
                _ownDeck.Add(new HeistEligibleCard(id, rank));
            return this;
        }
    }

    /// <summary> Server-side derivations the invariant checks compare the public state against. </summary>
    public static class SecretDerivations
    {
        /// <summary>
        /// The unseen pool as it must be: every card of this seat's that is still in a deck or a hand and has
        /// never become public, sorted canonically. The public member is maintained incrementally by
        /// <see cref="ZoneOps"/>; this is what it is checked against.
        /// </summary>
        public static List<CardId> DeriveUnseenPool(MatchModel match, int seat)
        {
            List<CardId> derived = new List<CardId>();
            SeatSecrets  secret  = match.SecretSeat(seat);
            if (secret == null)
                return derived;

            foreach (CardInstance instance in match.Rules.Instances)
            {
                if (instance.Owner != seat || instance.IsPublic || instance.Place != CardPlace.Unseen)
                    continue;

                if (secret.Cards.TryGetValue(instance.Id, out HiddenCard hidden))
                    derived.Add(hidden.CardId);
            }

            ZoneOps.SortPool(derived);
            return derived;
        }

        /// <summary> The card's numbers at the rank its owner brought it, or null when this side cannot know. </summary>
        public static CardStats? Stats(MatchModel match, CardInstanceId id)
        {
            CardInfo card = CardLookup.Info(match, id);
            if (card == null)
                return null;

            return card.GetStatsAtRank(CardLookup.Rank(match, id));
        }
    }

    /// <summary> Shorthand for driving a match in a test. </summary>
    public static class EngineTestExtensions
    {
        public static MatchIntentResult Play(this MatchEngine engine, int seat, CardInstanceId card, EffectTargetRef target = default(EffectTargetRef))
            => engine.Submit(seat, new PlayCardIntent(card, target));

        public static MatchIntentResult Attack(this MatchEngine engine, int seat, CardInstanceId attacker, EffectTargetRef target)
            => engine.Submit(seat, new AttackIntent(attacker, target));

        public static MatchIntentResult EndTurn(this MatchEngine engine, int seat)
            => engine.Submit(seat, new EndTurnIntent());

        public static MatchIntentResult Mulligan(this MatchEngine engine, int seat, params CardInstanceId[] replace)
            => engine.Submit(seat, new MulliganIntent(new List<CardInstanceId>(replace)));

        /// <summary>
        /// Answer a held peek by naming cards, which is what a fixture wants to read — converted to the
        /// positions the intent actually carries, the way the board's own confirm does it.
        /// </summary>
        public static MatchIntentResult ChooseKept(this MatchEngine engine, int seat, params CardInstanceId[] keep)
        {
            List<CardInstanceId> revealed = engine.Revealed(seat);
            List<int>            indices  = new List<int>(keep.Length);

            for (int ndx = 0; ndx < revealed.Count; ndx++)
            {
                if (System.Array.IndexOf(keep, revealed[ndx]) >= 0)
                    indices.Add(ndx);
            }

            return engine.Submit(seat, new EffectChoiceIntent(engine.ChoiceId, indices));
        }

        public static BoardCritter Critter(this MatchEngine engine, CardInstanceId id) => engine.FindCritterAnywhere(id);

        /// <summary>
        /// Where a card actually is, which for a hidden card is a question only the authority can answer —
        /// the public entry says <see cref="CardPlace.Unseen"/> for both hidden zones. Every call site of the
        /// old <c>ZoneOf</c> was read rather than substituted, and every one of them wanted this.
        /// </summary>
        public static AuthorityZone ZoneOf(this MatchEngine engine, CardInstanceId id) => AuthorityZones.Of(engine.Model, id);

        /// <summary> Where a card is as far as everybody may know: the public half of the same question. </summary>
        public static CardPlace PlaceOf(this MatchEngine engine, CardInstanceId id) => engine.Rules.Instance(id).Place;

        /// <summary> One seat's secret hand, which is where a hand lives now. </summary>
        public static List<CardInstanceId> SecretHand(this MatchEngine engine, int seat) => engine.Model.SecretSeat(seat).Hand;

        /// <summary> One seat's secret deck. </summary>
        public static List<CardInstanceId> SecretDeck(this MatchEngine engine, int seat) => engine.Model.SecretSeat(seat).Deck;

        /// <summary> Whether this seat's secret hand holds the instance. </summary>
        public static bool InHand(this MatchEngine engine, int seat, CardInstanceId id)
            => engine.Model.SecretSeat(seat).Hand.Contains(id);

        /// <summary> Whether this seat's secret deck holds the instance. </summary>
        public static bool InDeck(this MatchEngine engine, int seat, CardInstanceId id)
            => engine.Model.SecretSeat(seat).Deck.Contains(id);

        /// <summary> What a held peek revealed to its owner, which is the seat's secret too. </summary>
        public static List<CardInstanceId> Revealed(this MatchEngine engine, int seat)
            => SecretOps.PeekRevealed(engine.Model, seat);

        /// <summary> Advance to the seat's next turn. </summary>
        public static void PassTurn(this MatchEngine engine)
            => engine.EndTurn(engine.Rules.SeatOnTurn);
    }
}
