using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests that the data a seat receives does not depend on hidden state. Two deals give seat 0 the same hand,
    /// the same played cards and the same trump card, and differ in everything else. The bytes seat 0 receives
    /// must be identical for both deals, so a card count, an ordering or an RNG position that depends on hidden
    /// state also fails.
    /// <para>
    /// Each check has a negative control that must fail, because comparing two payloads built from the same
    /// hidden state would also report them identical.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchSecrecyTests
    {
        // The two deals use different RNG seeds as well as different cards, so a leaked RNG position also fails
        // the comparison.
        const ulong SeedA = 111UL;
        const ulong SeedB = 222UL;

        #region The two indistinguishable deals

        // The same in both deals: seat 0's hand, the card each other seat plays into the first trick, and the
        // trump card.
        static Card[] Seat0Hand => new Card[]
        {
            MatchTestDeals.Hearts(Rank.King), MatchTestDeals.Clubs(Rank.Three), MatchTestDeals.Clubs(Rank.Four),
            MatchTestDeals.Clubs(Rank.Five), MatchTestDeals.Clubs(Rank.Six),
        };

        static Card Seat1Play  => MatchTestDeals.Hearts(Rank.Three);
        static Card Seat2Play  => MatchTestDeals.Hearts(Rank.Four);
        static Card Seat3Play  => MatchTestDeals.Hearts(Rank.Five);
        static Card TrumpCard  => MatchTestDeals.Spades(Rank.Two);

        /// <summary>
        /// Deal A. Besides the heart it plays, each other seat holds diamonds. The undealt cards differ from
        /// deal B's.
        /// </summary>
        static List<Card> DeckA() => DeckAWith(Seat0Hand, Seat3Play);

        /// <summary>Deal A with seat 0's hand and the heart seat 3 holds and plays replaced.</summary>
        static List<Card> DeckAWith(Card[] seat0Hand, Card seat3Card) => MatchTestDeals.BuildDeck(
            seat0: seat0Hand,
            seat1: new Card[] { Seat1Play, MatchTestDeals.Diamonds(Rank.Two), MatchTestDeals.Diamonds(Rank.Three), MatchTestDeals.Diamonds(Rank.Four), MatchTestDeals.Diamonds(Rank.Five) },
            seat2: new Card[] { Seat2Play, MatchTestDeals.Diamonds(Rank.Six), MatchTestDeals.Diamonds(Rank.Seven), MatchTestDeals.Diamonds(Rank.Eight), MatchTestDeals.Diamonds(Rank.Nine) },
            seat3: new Card[] { seat3Card, MatchTestDeals.Diamonds(Rank.Ten), MatchTestDeals.Diamonds(Rank.Jack), MatchTestDeals.Diamonds(Rank.Queen), MatchTestDeals.Diamonds(Rank.King) },
            trumpCard: TrumpCard);

        /// <summary>
        /// Deal B. Seat 0's hand, the played cards and the trump card match deal A. Every other seat holds
        /// different unplayed cards than in deal A.
        /// </summary>
        static List<Card> DeckB() => MatchTestDeals.BuildDeck(
            seat0: Seat0Hand,
            seat1: new Card[] { Seat1Play, MatchTestDeals.Clubs(Rank.Seven), MatchTestDeals.Clubs(Rank.Eight), MatchTestDeals.Clubs(Rank.Nine), MatchTestDeals.Clubs(Rank.Ten) },
            seat2: new Card[] { Seat2Play, MatchTestDeals.Clubs(Rank.Jack), MatchTestDeals.Clubs(Rank.Queen), MatchTestDeals.Clubs(Rank.King), MatchTestDeals.Clubs(Rank.Ace) },
            seat3: new Card[] { Seat3Play, MatchTestDeals.Spades(Rank.Three), MatchTestDeals.Spades(Rank.Four), MatchTestDeals.Spades(Rank.Five), MatchTestDeals.Spades(Rank.Six) },
            trumpCard: TrumpCard);

        /// <summary>
        /// An engine on the given deal with the first trick played and resolved. Seat 0 wins the trick, so seat 0
        /// is on turn when the payloads are compared.
        /// </summary>
        static MatchEngine AtTheSamePosition(ulong seed, List<Card> deck, Card seat3Card)
        {
            MatchEngine engine = MatchTestDeals.Engine(seed, deck, startingLeaderSeat: 0);
            MatchTestDeals.PlayInOrder(engine, MatchTestDeals.Hearts(Rank.King), Seat1Play, Seat2Play, seat3Card);
            engine.Advance(engine.ResolvePauseEndsAt);
            return engine;
        }

        static MatchEngine EngineA() => AtTheSamePosition(SeedA, DeckA(), Seat3Play);
        static MatchEngine EngineB() => AtTheSamePosition(SeedB, DeckB(), Seat3Play);

        #endregion

        #region The payloads

        static byte[] SeatPayloadBytes(MatchEngine engine, int seat)
            => MetaSerialization.SerializeTagged(MatchSeatView.ForSeat(engine, seat), MetaSerializationFlags.IncludeAll, logicVersion: null);

        static byte[] PublicProjectionBytes(MatchEngine engine)
            => MetaSerialization.SerializeTagged(MatchBoard.Build(engine), MetaSerializationFlags.IncludeAll, logicVersion: null);

        /// <summary>
        /// The whole replicated model, serialized with the flags the SDK uses when sending it to a subscriber.
        /// This is the real network payload, so a member that should be server-only but is not shows up in it.
        /// </summary>
        static byte[] ReplicatedModelBytes(MatchEngine engine)
            => MetaSerialization.SerializeTagged<IMultiplayerModel>(
                MatchTestDeals.Model(engine, MatchTestDeals.Seat0PlayerId), MetaSerializationFlags.SendOverNetwork, logicVersion: null);

        /// <summary>The same model serialized for checksumming. A leak could also appear under this mask.</summary>
        static byte[] ChecksummedModelBytes(MatchEngine engine)
            => MetaSerialization.SerializeTagged<IMultiplayerModel>(
                MatchTestDeals.Model(engine, MatchTestDeals.Seat0PlayerId), MetaSerializationFlags.ComputeChecksum, logicVersion: null);

        static byte[] PrivateStateBytes(MatchEngine engine, int seat)
        {
            MatchModel model = MatchTestDeals.Model(engine, MatchTestDeals.Seat0PlayerId);
            MatchMemberPrivateState state = new MatchMemberPrivateState(
                MatchTestDeals.Seat0PlayerId, seat, model.Engine.GetHand(seat), model.Board.PlayIndex);
            return MetaSerialization.SerializeTagged(state, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
        }

        /// <summary>
        /// The public projection with seat 1's hand appended. Only the negative control uses it, to show that
        /// the byte comparison detects a leaked hand.
        /// </summary>
        static byte[] LeakyProjectionBytes(MatchEngine engine)
        {
            byte[] honest = PublicProjectionBytes(engine);
            byte[] leaked = MetaSerialization.SerializeTagged(new List<Card>(engine.GetHand(1)), MetaSerializationFlags.IncludeAll, logicVersion: null);

            byte[] combined = new byte[honest.Length + leaked.Length];
            honest.CopyTo(combined, 0);
            leaked.CopyTo(combined, honest.Length);
            return combined;
        }

        #endregion

        #region The invariant

        [Test]
        public void TheTwoDealsReallyAreIndistinguishableToSeatZero()
        {
            // Check the premise of this fixture: same hand, same public history, same trump, same seat on turn.
            MatchEngine a = EngineA();
            MatchEngine b = EngineB();

            Assert.That(b.GetHand(0), Is.EqualTo(a.GetHand(0)), $"seeds {SeedA} and {SeedB}");
            Assert.That(b.Plays, Is.EqualTo(a.Plays), $"seeds {SeedA} and {SeedB}");
            Assert.That(b.TrumpCard, Is.EqualTo(a.TrumpCard), $"seeds {SeedA} and {SeedB}");
            Assert.That(b.SeatOnTurn, Is.EqualTo(a.SeatOnTurn), $"seeds {SeedA} and {SeedB}");
        }

        [Test]
        public void SeatZerosPayloadIsByteIdenticalAcrossDealsItCannotTellApart()
        {
            Assert.That(SeatPayloadBytes(EngineB(), 0), Is.EqualTo(SeatPayloadBytes(EngineA(), 0)), $"seeds {SeedA} and {SeedB}");
        }

        [Test]
        public void ThePublicProjectionIsByteIdenticalAcrossDealsSeatZeroCannotTellApart()
        {
            Assert.That(PublicProjectionBytes(EngineB()), Is.EqualTo(PublicProjectionBytes(EngineA())), $"seeds {SeedA} and {SeedB}");
        }

        [Test]
        public void TheReplicatedModelIsByteIdenticalAcrossDealsNoSeatCanTellApart()
        {
            // Every client receives the same replicated model, so this covers every seat, not just seat 0. The
            // two deals differ only in state that no seat may see.
            Assert.That(ReplicatedModelBytes(EngineB()), Is.EqualTo(ReplicatedModelBytes(EngineA())), $"seeds {SeedA} and {SeedB}");
        }

        [Test]
        public void TheChecksummedModelIsByteIdenticalAcrossDealsNoSeatCanTellApart()
        {
            // A member marked only Hidden is still checksummed, and the desync diagnostic serializes with a mask
            // that excludes only the checksum flag. This test enforces that secret members use ServerOnly rather
            // than Hidden alone.
            Assert.That(ChecksummedModelBytes(EngineB()), Is.EqualTo(ChecksummedModelBytes(EngineA())), $"seeds {SeedA} and {SeedB}");
        }

        [Test]
        public void SeatZerosPrivateStateIsByteIdenticalAcrossDealsItCannotTellApart()
        {
            Assert.That(PrivateStateBytes(EngineB(), 0), Is.EqualTo(PrivateStateBytes(EngineA(), 0)), $"seeds {SeedA} and {SeedB}");
        }

        [Test]
        public void TheReplicatedModelCarriesNoHand()
        {
            // Deserializing the network payload gives the model a client holds, so this checks directly that the
            // engine does not reach the client.
            MatchModel sent = MetaSerialization.DeserializeTagged<IMultiplayerModel>(
                ReplicatedModelBytes(EngineA()), MetaSerializationFlags.SendOverNetwork, resolver: null, logicVersion: null) as MatchModel;

            Assert.That(sent, Is.Not.Null);
            Assert.That(sent.Engine, Is.Null, "the authoritative engine reached a client");
            Assert.That(sent.OwnSeat, Is.EqualTo(-1), "a hand reached a client on the timeline");
            Assert.That(sent.GetOwnHand(), Is.Empty, "a hand reached a client on the timeline");
            Assert.That(sent.Board.Plays, Is.EqualTo(EngineA().Plays), "the public play history did not survive the wire");
        }

        [Test]
        public void ABotAtSeatZeroPlaysTheSameCardInBothDeals()
        {
            // A bot decides from the seat view only, so it must play the same card when the hidden state differs.
            MatchSeatView viewA = MatchSeatView.ForSeat(EngineA(), 0);
            MatchSeatView viewB = MatchSeatView.ForSeat(EngineB(), 0);

            foreach (BotProfile profile in TestBotConfig.All)
            {
                for (ulong seed = 0; seed < 50UL; seed++)
                    Assert.That(BotPolicy.ChooseCard(viewB, profile, seed), Is.EqualTo(BotPolicy.ChooseCard(viewA, profile, seed)), $"profile {profile}, seed {seed}");
            }
        }

        [Test]
        public void TheSeatViewCarriesExactlyOneHandAndThePublicProjectionCarriesNone()
        {
            // Structural check: the types have no field where a second hand could be stored. Any field whose type
            // contains Card at any depth counts, so a hand wrapped in a dictionary or nested generic is caught.
            // PlayRecord is exempt because played cards are public (docs/game-rules.md, "Public and private
            // information").
            Assert.That(CountCardCollectionFields(typeof(MatchSeatView)), Is.EqualTo(1), $"{nameof(MatchSeatView)} holds more than one collection of cards");
            Assert.That(CountCardCollectionFields(typeof(MatchBoard)), Is.EqualTo(0), $"{nameof(MatchBoard)} holds a collection of cards");
        }

        [Test]
        public void TheReplicatedModelCarriesNoCardsOutsideItsServerOnlyMembers()
        {
            // MatchModel is the type sent to clients, so it gets the same structural check. Only replicated
            // members count, because the server-only engine is meant to hold every hand.
            Assert.That(CountReplicatedCardCollectionFields(typeof(MatchModel)), Is.EqualTo(0),
                $"{nameof(MatchModel)} replicates a collection of cards");
        }

        /// <summary>
        /// Count the fields that can hold more than one card. A single <see cref="Card"/> field does not count
        /// because the trump card is public. A field of any type that contains a Card at any depth counts.
        /// </summary>
        static int CountCardCollectionFields(System.Type type)
        {
            int count = 0;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(Card) && MentionsCard(field.FieldType))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Like <see cref="CountCardCollectionFields"/>, but counts only replicated members. A member marked
        /// <see cref="MetaMemberFlags.ServerOnly"/> is persisted but never sent or checksummed, so the engine
        /// behind one is not a leak.
        /// <para>
        /// Properties are checked as well as fields. <c>[MetaMember]</c> on an auto-property is on the property,
        /// not on the compiler-generated backing field, so checking fields only would skip every member declared
        /// as an auto-property.
        /// </para>
        /// </summary>
        static int CountReplicatedCardCollectionFields(System.Type type)
        {
            const BindingFlags DeclaredMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            int count = 0;

            foreach (FieldInfo field in type.GetFields(DeclaredMembers))
            {
                if (IsReplicatedMember(field) && IsCardCollection(field.FieldType))
                    count++;
            }

            // A property backed by an explicit [MetaMember] field is counted once, through the field, because the
            // property has no attribute.
            foreach (PropertyInfo property in type.GetProperties(DeclaredMembers))
            {
                if (IsReplicatedMember(property) && IsCardCollection(property.PropertyType))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Whether the member is sent to clients: it has <c>[MetaMember]</c> and is not server-only. The
        /// server-only flag can be set inline, as in <c>[MetaMember(n, flags)]</c>, or as a separate attribute, as
        /// in <c>[MetaMember(4), ServerOnly]</c>, so both are checked.
        /// </summary>
        static bool IsReplicatedMember(MemberInfo member)
        {
            MetaMemberAttribute meta = member.GetCustomAttribute<MetaMemberAttribute>();
            if (meta == null)
                return false;

            MetaMemberFlags flags = meta.Flags;
            foreach (MetaMemberFlagAttribute flagAttribute in member.GetCustomAttributes<MetaMemberFlagAttribute>(inherit: false))
                flags |= flagAttribute.Flags;

            return (flags & MetaMemberFlags.ServerOnly) == 0;
        }

        /// <summary>
        /// Whether the type can hold more than one card. A single <see cref="Card"/> is public, so it does not count.
        /// </summary>
        static bool IsCardCollection(System.Type type) => type != typeof(Card) && MentionsCard(type);

        static bool MentionsCard(System.Type type) => MentionsCard(type, new HashSet<System.Type>());

        static bool MentionsCard(System.Type type, HashSet<System.Type> visited)
        {
            if (type == typeof(PlayRecord))
                return false;
            if (type == typeof(Card))
                return true;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                return false;

            // Visit each type once per top-level call, so a type graph with cycles terminates.
            if (!visited.Add(type))
                return false;

            if (type.IsArray)
                return MentionsCard(type.GetElementType(), visited);

            if (type.IsGenericType)
            {
                foreach (System.Type argument in type.GetGenericArguments())
                {
                    if (MentionsCard(argument, visited))
                        return true;
                }
            }

            // Check the fields of a class or struct as well. Without this, a type that holds a hand, such as the
            // engine or a hand delivery, would read as holding no cards, and that is how a real leak would look.
            // A field that is a single Card is skipped at every depth because the trump card is public
            // (docs/game-rules.md, "Public and private information").
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(Card) && MentionsCard(field.FieldType, visited))
                    return true;
            }

            return false;
        }

        #endregion

        #region Negative controls

        [Test]
        public void NegativeControl_TheHiddenStateOfTheTwoDealsDiffers()
        {
            // Without this check the fixture could be comparing a deal with itself, and every equality test above
            // would pass without testing anything.
            MatchEngine a = EngineA();
            MatchEngine b = EngineB();

            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
                Assert.That(b.GetHand(seat), Is.Not.EqualTo(a.GetHand(seat)), $"seat {seat} holds the same cards in both deals");

            byte[] authoritativeA = MetaSerialization.SerializeTagged(a, MetaSerializationFlags.IncludeAll, logicVersion: null);
            byte[] authoritativeB = MetaSerialization.SerializeTagged(b, MetaSerializationFlags.IncludeAll, logicVersion: null);
            Assert.That(authoritativeB, Is.Not.EqualTo(authoritativeA), "the authoritative state — deck, undealt remainder and RNG position — is identical in both deals");
        }

        [Test]
        public void NegativeControl_AnotherSeatsPayloadDiffersAcrossTheTwoDeals()
        {
            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
                Assert.That(SeatPayloadBytes(EngineB(), seat), Is.Not.EqualTo(SeatPayloadBytes(EngineA(), seat)), $"seat {seat}: the comparison does not notice a different hand");
        }

        [Test]
        public void NegativeControl_TheSameModelWithNothingMaskedOutDiffersAcrossTheTwoDeals()
        {
            // Serialized with no members excluded, the two models differ. So the network and checksum comparisons
            // pass because the ServerOnly members are excluded, not because the models are the same.
            byte[] wholeA = MetaSerialization.SerializeTagged<IMultiplayerModel>(
                MatchTestDeals.Model(EngineA(), MatchTestDeals.Seat0PlayerId), MetaSerializationFlags.IncludeAll, logicVersion: null);
            byte[] wholeB = MetaSerialization.SerializeTagged<IMultiplayerModel>(
                MatchTestDeals.Model(EngineB(), MatchTestDeals.Seat0PlayerId), MetaSerializationFlags.IncludeAll, logicVersion: null);

            Assert.That(wholeB, Is.Not.EqualTo(wholeA), "the model carries no hidden state at all, so masking it proves nothing");
        }

        [Test]
        public void NegativeControl_AnotherSeatsPrivateStateDiffersAcrossTheTwoDeals()
        {
            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
                Assert.That(PrivateStateBytes(EngineB(), seat), Is.Not.EqualTo(PrivateStateBytes(EngineA(), seat)), $"seat {seat}: the private-state comparison does not notice a different hand");
        }

        [Test]
        public void NegativeControl_AProjectionWithAHandInItFailsTheComparison()
        {
            Assert.That(LeakyProjectionBytes(EngineB()), Is.Not.EqualTo(LeakyProjectionBytes(EngineA())),
                "a projection carrying another seat's hand compared equal, so the test would not catch a leak");
        }

        [Test]
        public void NegativeControl_ADifferentHandForSeatZeroChangesItsPayload()
        {
            // Deal C is deal A with one card of seat 0's hand swapped for an undealt card. No public state changes,
            // so only seat 0's own payload should differ.
            List<Card> deckC = DeckAWith(
                new Card[]
                {
                    MatchTestDeals.Hearts(Rank.King), MatchTestDeals.Clubs(Rank.Three), MatchTestDeals.Clubs(Rank.Four),
                    MatchTestDeals.Clubs(Rank.Five), MatchTestDeals.Clubs(Rank.Seven),
                },
                Seat3Play);

            MatchEngine c = AtTheSamePosition(SeedA, deckC, Seat3Play);

            Assert.That(PublicProjectionBytes(c), Is.EqualTo(PublicProjectionBytes(EngineA())), "swapping a card in seat 0's hand should not move the public board");
            Assert.That(SeatPayloadBytes(c, 0), Is.Not.EqualTo(SeatPayloadBytes(EngineA(), 0)), "the payload did not notice seat 0's own hand changing");

            // Deals A and B give seat 0 the same hand, so they cannot detect seat 0's hand leaking into the
            // replicated model. Deal C can: no public state changed, so any difference in the replicated model
            // means it carries seat 0's hand.
            Assert.That(ReplicatedModelBytes(c), Is.EqualTo(ReplicatedModelBytes(EngineA())),
                "the replicated model moved when only seat 0's own hand changed, so it is carrying that hand");
            Assert.That(ChecksummedModelBytes(c), Is.EqualTo(ChecksummedModelBytes(EngineA())),
                "the checksummed model moved when only seat 0's own hand changed, so it is carrying that hand");
        }

        /// <summary>
        /// A type with the kind of leak the structural check must catch, declared with auto-properties like most
        /// model members. <c>[MetaMember]</c> is on the property and not on the backing field, so a check that
        /// read fields only would find no cards here.
        /// </summary>
        [MetaSerializable]
        public class LeakyMemberDeclarations
        {
            /// <summary>A replicated hand declared as a property. The check must count it.</summary>
            [MetaMember(1)] public List<Card> Hand { get; private set; }

            /// <summary>
            /// A replicated hand wrapped in its own type rather than a collection. The check must count it.
            /// </summary>
            [MetaMember(2)] public MatchOwnHand Delivery { get; private set; }

            /// <summary>Server-only, so it is not a leak.</summary>
            [MetaMember(3), ServerOnly] public List<Card> AuthoritativeHand { get; private set; }

            /// <summary>A single public card, like the trump card, so it is not a leak.</summary>
            [MetaMember(4)] public Card TrumpCard { get; private set; }

            /// <summary>Not serialized at all, so it never reaches a client.</summary>
            public List<Card> Scratch { get; private set; }
        }

        [Test]
        public void NegativeControl_TheStructuralCheckCountsCardsDeclaredAsProperties()
        {
            // Without this control, the structural check on MatchModel could pass even for a model that
            // replicated a hand.
            Assert.That(CountReplicatedCardCollectionFields(typeof(LeakyMemberDeclarations)), Is.EqualTo(2),
                "the replicated-member check does not see cards behind a property, a member type, or both");
        }

        [Test]
        public void NegativeControl_ATypeThatMerelyHoldsAHandIsCountedAsCarryingCards()
        {
            // If the engine were replicated it would leak every hand, yet it is not itself a collection of cards.
            // It is a class that holds the hands.
            Assert.That(IsCardCollection(typeof(MatchEngine)), Is.True, "a type holding every hand reads as carrying no cards");
            Assert.That(IsCardCollection(typeof(MatchOwnHand)), Is.True, "a hand delivery reads as carrying no cards");

            // The check must not flag the public board or a single card, or it would report a leak on every board.
            Assert.That(IsCardCollection(typeof(MatchBoard)), Is.False, "the public board reads as carrying cards");
            Assert.That(IsCardCollection(typeof(Card)), Is.False, "the revealed trump card reads as a hand");
        }

        [Test]
        public void NegativeControl_ADifferentPlayedCardChangesThePublicProjection()
        {
            // Deal D is deal A with seat 3 holding and playing a different heart. The same seat still wins the
            // trick, so the only difference is one card in the play history.
            List<Card> deckD = DeckAWith(Seat0Hand, MatchTestDeals.Hearts(Rank.Six));

            MatchEngine d = AtTheSamePosition(SeedA, deckD, MatchTestDeals.Hearts(Rank.Six));

            Assert.That(d.TrickWinnerSeats, Is.EqualTo(EngineA().TrickWinnerSeats), "the control was meant to change one card, not the result");
            Assert.That(PublicProjectionBytes(d), Is.Not.EqualTo(PublicProjectionBytes(EngineA())), "the public projection did not notice a different card being played");
            Assert.That(SeatPayloadBytes(d, 0), Is.Not.EqualTo(SeatPayloadBytes(EngineA(), 0)), "seat 0's payload did not notice a different card being played");
        }

        #endregion
    }
}
