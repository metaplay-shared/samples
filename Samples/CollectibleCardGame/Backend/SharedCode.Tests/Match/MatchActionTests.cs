using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The match timeline's one-writer guarantee, the replay rule, and the shape rules that make it hard to
    /// feed <c>ServerOnly</c> state into a public mutation by accident.
    /// <para>
    /// Everything here fails <em>silently</em>: an action that gained the client-issuable flag would open the
    /// whole timeline with no symptom until somebody exploited it — and the stakes rose with this refactor,
    /// because such an action would not write a projection, it would play a move. An action that carried a
    /// <c>ServerOnly</c> member would work perfectly on the server and default on every follower.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchActionTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        static IEnumerable<Type> MatchActionTypes()
        {
            foreach (Type type in typeof(MatchAction).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(MatchAction).IsAssignableFrom(type))
                    continue;

                yield return type;
            }
        }

        static IEnumerable<MemberInfo> PayloadMembersOf(Type action)
        {
            foreach (PropertyInfo property in action.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.DeclaringType != typeof(ModelAction) && property.DeclaringType != typeof(object))
                    yield return property;
            }

            foreach (FieldInfo field in action.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                yield return field;
        }

        static Type TypeOf(MemberInfo member)
            => member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

        [Test]
        public void EveryMatchActionIsLeaderSynchronizedOnly()
        {
            foreach (Type type in MatchActionTypes())
            {
                ModelActionSpec spec = ModelActionRepository.Instance.SpecFromType[type];

                Assert.That(spec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.LeaderSynchronized), Is.True,
                    $"{type.Name} must be issuable by the actor");
                Assert.That(spec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized), Is.False,
                    $"{type.Name} must not be enqueueable by a client: the SDK's validation hook defaults to allow, so this flag is the lock");
                Assert.That(spec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerUnsynchronized), Is.False,
                    $"{type.Name} must not run at a different timeline position on the client: the model is checksummed");
            }
        }

        [Test]
        public void NoMatchActionCarriesTheAuthoritativeState()
        {
            // An action is broadcast verbatim and re-executed on every client's copy of the model, where every
            // hidden member is default. An action that carried the secret — or a public rules object, which
            // has no business riding a payload either — would be a second implementation of the rules living
            // on the action layer.
            foreach (Type type in MatchActionTypes())
            {
                foreach (MemberInfo member in PayloadMembersOf(type))
                    AssertNotAuthoritative(type, member.Name, TypeOf(member));
            }
        }

        static void AssertNotAuthoritative(Type action, string member, Type memberType)
        {
            Assert.That(memberType, Is.Not.EqualTo(typeof(MatchSecrets)), $"{action.Name}.{member} carries the secret root");
            Assert.That(memberType, Is.Not.EqualTo(typeof(SeatSecrets)), $"{action.Name}.{member} carries a seat's hidden half");
            Assert.That(memberType, Is.Not.EqualTo(typeof(HiddenCard)), $"{action.Name}.{member} carries a hidden identity");
            Assert.That(memberType, Is.Not.EqualTo(typeof(RandomPCG)), $"{action.Name}.{member} carries the seeded stream");
            Assert.That(memberType, Is.Not.EqualTo(typeof(MatchRulesState)), $"{action.Name}.{member} carries the whole rules state");
            Assert.That(memberType, Is.Not.EqualTo(typeof(SeatState)), $"{action.Name}.{member} carries a seat's rules state");
            Assert.That(memberType, Is.Not.EqualTo(typeof(CardInstance)), $"{action.Name}.{member} carries a card instance");
            Assert.That(memberType, Is.Not.EqualTo(typeof(List<HandCard>)), $"{action.Name}.{member} carries a hand");
        }

        [Test]
        public void NoMatchActionDeclaresAServerOnlyMember()
        {
            // The masks are per member at any depth, so a [ServerOnly] member on an action WOULD be stripped
            // on the wire and default on the follower — which would let a server-authored action carry a
            // secret parameter. Nothing documents whether a leader-side action is guaranteed not to be
            // re-serialized, no SDK sample does it, and the SDK's own guild feature solves the same problem a
            // different way. So a secret the leader needs rides an [IgnoreDataMember] field instead, and this refuses the
            // shortcut rather than trusting a comment.
            //
            // It also happens to close the most tempting way to smuggle the secret into a public mutation.
            foreach (Type type in MatchActionTypes())
            {
                if (!MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec) || spec.Members == null)
                    continue;

                foreach (MetaSerializableMember member in spec.Members)
                {
                    Assert.That(member.Flags.HasFlag(MetaMemberFlags.Hidden), Is.False,
                        $"{type.Name}.{member.Name} is ServerOnly; an action's payload must be public in full");
                }
            }
        }

        [Test]
        public void OnlyTheMulliganKeepsAMemberOffTheWire()
        {
            // Actions use implicit members, so every field is serialized unless it says otherwise. That
            // makes [IgnoreDataMember] a second door into "an action with a parameter the wire never sees" —
            // the first being [ServerOnly], which the test above refuses outright.
            //
            // It is open on purpose and exactly once: which cards a mulligan named. Those are instance ids,
            // they may not reach a payload, and the seat that named them is the only one who may know.
            //
            // The peek does not need it, and that is the interesting half. Its answer names positions in the
            // reveal rather than cards, so there is nothing to hide — which is the shape to reach for first.
            // A mulligan cannot follow, because its set must survive until MatchMulliganResolve and a
            // position is only meaningful against the hand it indexed.
            //
            // The misuse it invites is caught rather than merely forbidden: FollowerMirror clones every
            // action through SendOverNetwork before re-executing it, so a public mutation that leaned on an
            // ignored member diverges on the first game.
            List<string> ignored = new List<string>();

            foreach (Type type in MatchActionTypes())
            {
                foreach (MemberInfo member in PayloadMembersOf(type))
                {
                    if (member.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                        ignored.Add($"{type.Name}.{member.Name}");
                }
            }

            Assert.That(ignored, Is.EquivalentTo(new[] { $"{nameof(MatchMulliganSubmit)}._replace" }));
        }

        [Test]
        public void OnlyThreeActionsNameACardAndEachHasAReason()
        {
            // Every card that reaches a payload is public from that moment, so the set of actions that carry
            // one is the set of places an identity can become public. It is three, each for its own reason,
            // and a fourth is either a leak or a new reveal path — both worth a stop.
            //
            //   MatchPlayCard  — the reveal. Playing a card is the only rule that makes an identity public.
            //   MatchDebugWin  — the developer shortcut, which ends a match that may have had nothing played
            //                    and reveals both starting decks so the Heist has a lineup at all. Refused
            //                    outside a development environment.
            //   MatchSetResult — the outcome, naming cards that were played and picked. Both are already
            //                    public by the time it is issued.
            //
            // The walk sees through collections and into the game's own structs. It did not until
            // MatchDebugWin's List<HeistEligibleCard> was found passing a check that only ever looked at the
            // member's own type.
            Type[] mayNameACard = { typeof(MatchPlayCard), typeof(MatchDebugWin), typeof(MatchSetResult) };

            foreach (Type type in MatchActionTypes())
            {
                // An addressed action is exempt, and naming a card is its entire purpose: it is delivered to
                // one seat and nobody else executes it, so the card it names reaches only the seat already
                // entitled to know. The rule being enforced here is about the payloads every follower sees.
                if (typeof(MatchAddressedAction).IsAssignableFrom(type))
                    continue;

                foreach (MemberInfo member in PayloadMembersOf(type))
                {
                    if (!NamesACard(TypeOf(member)))
                        continue;

                    Assert.That(mayNameACard, Does.Contain(type),
                        $"{type.Name}.{member.Name} names a card, and only {string.Join(", ", mayNameACard.Select(t => t.Name))} may");
                }
            }
        }

        /// <summary> Whether this type is, contains, or is a collection of, something that names a card. </summary>
        static bool NamesACard(Type type)
        {
            if (type == typeof(CardId) || type == typeof(MetaRef<CardInfo>) || type == typeof(CardInfo))
                return true;

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    if (NamesACard(argument))
                        return true;
                }

                return false;
            }

            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.Namespace?.StartsWith("System") == true)
                return false;

            // A struct or class of the game's own: look at what it holds.
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (NamesACard(field.FieldType))
                    return true;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (NamesACard(property.PropertyType))
                    return true;
            }

            return false;
        }

        [Test]
        public void NoMatchActionCarriesATime()
        {
            // Every stamp is derived from the model's own clock, which a follower has at the same tick. A time
            // on a payload would be the host's clock leaking onto the timeline beside it.
            foreach (Type type in MatchActionTypes())
            {
                foreach (MemberInfo member in PayloadMembersOf(type))
                {
                    Type memberType = TypeOf(member);
                    Assert.That(memberType == typeof(MetaTime) || memberType == typeof(MetaTime?), Is.False,
                        $"{type.Name}.{member.Name} carries a time on the payload");
                }
            }
        }

        /// <summary>
        /// Checking changes nothing at all — for every path that checks, in a state where it <b>passes</b>,
        /// which is the only place the assertion is worth anything.
        /// <para>
        /// The bug class is one <c>match.Emit</c> or one mutation inside what is supposed to be a question:
        /// it would corrupt the history and, because the actor asks before it stages, corrupt it for an
        /// action that was then never issued. An action refused early never reaches the rest of its own body,
        /// so asking all of them against one freshly dealt model would prove it for the two the mulligan
        /// phase admits and prove nothing for the rest — which is what this fixture did until a review
        /// instrumented it and counted.
        /// </para>
        /// </summary>
        [Test]
        public void CheckingAnyPathChangesNothingAtAll()
        {
            foreach ((string what, MatchModel model, MatchIntent intent, MatchHostAction host) in EveryCheckedPathInAStateThatAdmitsIt())
            {
                byte[] before    = DealtModels.Checksummed(model);
                byte[] persisted = DealtModels.Everything(model);
                int    history   = model.History.Count;

                MatchIntentResult gate = intent != null
                    ? intent.Prepare(model, 0, out MatchAction _)
                    : host.ServerPrepare(model);

                // It has to pass, or the assertions below are true by construction. This is the half a review
                // found missing: six of eight were being refused before they got anywhere.
                Assert.That(gate.IsSuccess, Is.True, $"{what}: the fixture's state no longer admits this ({gate})");

                Assert.That(DealtModels.Checksummed(model), Is.EqualTo(before), $"{what}: checking moved checksummed state");
                Assert.That(DealtModels.Everything(model), Is.EqualTo(persisted), $"{what}: checking moved the secret");
                Assert.That(model.History.Count, Is.EqualTo(history), $"{what}: checking recorded an event");
            }
        }

        /// <summary>
        /// Every path that checks before it stages, paired with a model whose state admits it: an intent for
        /// the five a seat can ask for, a host action for the rest. Exactly one of the two is set.
        /// </summary>
        static IEnumerable<(string, MatchModel, MatchIntent, MatchHostAction)> EveryCheckedPathInAStateThatAdmitsIt()
        {
            SharedGameConfig config = TestGameConfig.Shared;

            // The mulligan, which is where a deal starts.
            MatchModel dealt = DealtModels.Build(config, dealSeed: 8080ul, botSeed: 1ul);
            yield return ("MulliganIntent", dealt, new MulliganIntent(new List<CardInstanceId>()), null);
            yield return ("MatchMulliganResolve", dealt, null, new MatchMulliganResolve());

            // A turn in progress: a card in hand this seat can afford, and an awake critter on the board.
            MatchEngine    playing = Playing(config, MatchTimings.Instant);
            CardInstanceId inHand  = playing.SecretHand(0)[0];
            CardInstanceId onBoard = playing.Rules.Seat(0).Board[0].Id;

            yield return ("PlayCardIntent", playing.Model,
                new PlayCardIntent(inHand, EffectTargetRef.None), null);
            yield return ("AttackIntent", playing.Model,
                new AttackIntent(onBoard, EffectTargetRef.Den(1)), null);
            yield return ("EndTurnIntent", playing.Model, new EndTurnIntent(), null);

            // A seat that has already acted this turn and still has bank left. Both halves are the rule.
            MatchEngine extending = Playing(config, MatchTimings.Default);
            extending.Play(0, extending.SecretHand(0)[0]);
            Assert.That(TurnRules.CanExtendFromReserve(extending.Model, 0), Is.True, "the fixture needs reserve left to extend from");
            yield return ("MatchExtendReserve", extending.Model, null, new MatchExtendReserve(0));

            // A held peek, answered by its owner. The one interactive resolution.
            MatchEngine peeking = Peeking(config);
            peeking.Play(0, peeking.SecretHand(0)[0], EffectTargetRef.None);
            Assert.That(peeking.Rules.PendingChoice, Is.Not.Null, "the fixture needs a held choice");
            yield return ("EffectChoiceIntent", peeking.Model,
                new EffectChoiceIntent(peeking.ChoiceId, new List<int> { 0 }), null);
        }

        static MatchEngine Playing(SharedGameConfig config, MatchTimings timings)
        {
            Scenario scenario = new Scenario(config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Board("TrailRabbit");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0).Build(timings);
        }

        static MatchEngine Peeking(SharedGameConfig config)
        {
            Scenario scenario = new Scenario(config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Deck("MeadowMouse");
            scenario.Seat(0).Deck("MooseWanderer");
            scenario.Seat(0).Deck("BusyBeaver");
            scenario.Seat(0).DeckOf(4, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0).Build();
        }

        [Test]
        public void ATableActionWithNoPayloadIsRefusedRatherThanApplied()
        {
            // Both staging paths ask ServerPrepare first — TrySubmitRule for a seat's intent, and
            // ExecuteMatchAction for the host's own actions — because the SDK's own "execute this action"
            // only warns on a refusal and has already staged it. That is only worth doing if a malformed
            // action actually refuses.
            //
            // These guards are reachable only through that call. They were once inside Execute, where the
            // SDK would reach them; moving them out means the path has to ask, and for a while the host's
            // path did not.
            MatchModel model = new MatchModel { GameConfig = Config, History = new List<MatchEvent>() };

            Assert.That(new MatchSetSeats(null).ServerPrepare(model).IsSuccess, Is.False);
            Assert.That(new MatchSetSeats(new List<MatchSeat> { new MatchSeat() }).ServerPrepare(model).IsSuccess, Is.False);
            Assert.That(new MatchSetResultAck(7).ServerPrepare(model).IsSuccess, Is.False);
            Assert.That(new MatchSetGrace(7, null).ServerPrepare(model).IsSuccess, Is.False);
            Assert.That(new MatchPushClocks(MetaDuration.Zero).ServerPrepare(model).IsSuccess, Is.False);
            Assert.That(new MatchSetResult(null, null).ServerPrepare(model).IsSuccess, Is.False);
            Assert.That(new MatchArmHeistDeadline(7, MetaDuration.FromSeconds(45)).ServerPrepare(model).IsSuccess, Is.False);
        }

        [Test]
        public void TheResultAcknowledgementIsAPublicMemberAnActionCanWrite()
        {
            // An action's Execute runs on every client against a model whose hidden members are default, so a
            // member an action writes has to be public. The acknowledgements are not secret — the losing
            // client showing "recording result…" is a feature.
            MatchModel model = new MatchModel
            {
                GameConfig  = Config,
                History     = new List<MatchEvent>(),
                ResultAcked = new List<bool> { false, false },
            };

            Assert.That(new MatchSetResultAck(1).InvokeExecute(model, commit: true).IsSuccess, Is.True);
            Assert.That(model.ResultAcked[1], Is.True);
            Assert.That(model.ResultAcked[0], Is.False);
        }

        [Test]
        public void TheMatchActionCodesAreInTheirOwnBlock()
        {
            // The 5100-5149 block is the match's. A code outside it would still work and would still be a
            // registry that no longer says where anything lives.
            foreach (Type type in MatchActionTypes())
            {
                ModelActionSpec spec = ModelActionRepository.Instance.SpecFromType[type];
                Assert.That(spec.TypeCode, Is.InRange(5100, 5149), $"{type.Name} has code {spec.TypeCode}, outside the match block");
            }
        }

        [Test]
        public void ThereAreEighteenMatchActions()
        {
            // Seven table actions, eight rule actions, three addressed deliveries and one server-authorized
            // developer shortcut.
            List<int> codes = new List<int>();
            foreach (Type type in MatchActionTypes())
                codes.Add(ModelActionRepository.Instance.SpecFromType[type].TypeCode);

            codes.Sort();
            Assert.That(codes, Is.EqualTo(new List<int>
            {
                ActionCodes.MatchSetSeats, ActionCodes.MatchSetPhase, ActionCodes.MatchSetResult,
                ActionCodes.MatchSetResultAck, ActionCodes.MatchSetGrace,
                ActionCodes.MatchPushClocks, ActionCodes.MatchArmHeistDeadline,
                ActionCodes.MatchMulliganSubmit, ActionCodes.MatchMulliganResolve, ActionCodes.MatchPlayCard,
                ActionCodes.MatchAttack, ActionCodes.MatchEndTurn, ActionCodes.MatchEffectChoice,
                ActionCodes.MatchExtendReserve,
                ActionCodes.MatchOwnCardGained, ActionCodes.MatchOwnPeekRevealed, ActionCodes.MatchOwnCardLost,
                ActionCodes.MatchDebugWin,
            }));
        }
    }
}
