# Engine Rulings

A rules engine cannot be silent. Every gap in [`game-design.md`](game-design.md) becomes a line of code that decides
something, so the gaps are resolved here as design rather than left to whoever writes the method — and each one is
pinned by a named test, because a silence resolved in code is a decision nobody can find later, while a silence
resolved in a named test shows up as a diff when somebody changes their mind.

Read this with [`rules.md`](rules.md), which is why the rules are shaped the way they are; this is only what they
decided. Tests live in `Backend/SharedCode.Tests/Engine/`; the **Test** column names the method, and the fixture is
the obvious one unless it says otherwise.

The **#** column is only this table's index; code and tests state the rule itself rather than citing it. The reasoning
behind the less obvious rulings is at the bottom.

## Guard

| # | Question | Ruling | Test |
|---|---|---|---|
| R1 | Do Guards stack? | No. Guard is a binary gate on reaching the Den; two Guards are one gate and two bodies. | `TwoGuardsGateExactlyLikeOne` |
| R2 | Does a Sneaky Guard gate the Den? | **No.** The gate counts only Guards the attacker could legally attack. Otherwise one Smoke Bomb on your own Guard makes it an unanswerable lock. | `SneakyGuardDoesNotProtectTheDen`, `ASmokeBombedGuardStopsGatingTheDen` |
| R3 | Does a sleepy Guard gate the Den? | Yes. Guard is a property of being in play. | `SleepyGuardStillProtectsTheDen` |
| R4 | Does Guard stop effect damage to the Den? | No — the keyword says "can't **attack** your Den". | `EffectDamageReachesTheDenThroughAGuard` |
| R4a | Does Guard redirect an attack onto itself? | No. It gates the Den and protects nothing else; damage lands where it was aimed. | `GuardNeverRedirectsDamageOntoItself` |

## Combat

| # | Question | Ruling | Test |
|---|---|---|---|
| R5 | Simultaneous death | Both damages applied before any death check; both die. | `MutuallyLethalTradeKillsBothCritters` |
| R6 | Death / Goodbye order | Canonical: the active seat's board ascending, then the other seat's. | `SimultaneousDeathsResolveActiveSeatFirst` |
| R7 | Counterattack when the attacker dies | Its damage still landed — the exchange is simultaneous. | `DyingAttackerStillDealtItsDamage` |
| R20 | May a 0-attack critter attack? | No. `CritterHasNoAttack`. | `ZeroAttackCritterIsRefused` |
| R21 | Does counterattacking spend the defender's own attack? | No. | `DefenderMayStillAttackOnItsOwnTurn` |
| R22 | What does waking clear? | `IsSleepy` and `HasAttackedThisTurn`. | `WakingClearsSleepyAndHasAttacked` |
| R42 | Attack your own critters? | Illegal. | `AttackingAFriendlyCritterIsRefused` |
| — | Board order | Cosmetic: a stable iteration order and nothing else. | `BoardOrderDoesNotAffectAnyOutcome` |

## Keywords

| # | Question | Ruling | Test |
|---|---|---|---|
| R8 | Snacktime on a dying attacker | Still heals. | `DyingSnacktimeCritterStillHealsItsDen` |
| R9 | Snacktime scope | Any damage the critter sources: attack, counterattack, or its own effect. | `SnacktimeHealsWhenDefendingToo`, `SnacktimeHealsForItsOwnEffectDamage` |
| R10 | Den heal cap | Capped at `DenStartingHp`. | `DenHealCapsAtStartingHp` |
| R11 | Critter heal cap | Capped at current `MaxHealth`. | `CritterHealCapsAtMaxHealth` |
| R12 | Bubble absorbs how much? | The whole first damage **instance**, not one point. | `BubbleAbsorbsTheWholeFirstDamageInstance` |
| R13 | Does 0 damage pop a Bubble? | No — above zero is required. | `ZeroDamageDoesNotPopABubble` |
| R14 | Bubble vs Bubble | Both pop, neither takes damage, neither dies. | `TwoBubbledCrittersTradeWithoutDamage` |
| R15 | Bubble vs effect damage | Consumed by the first damage from **any** source. | `BubbleAbsorbsEffectDamage` |
| R15a | Bubble vs a stat reduction | Does not pop. A stat change is not damage. | `StatReductionDoesNotPopABubble` |
| R16 | When does Sneaky drop? | When the critter deals damage above zero to anything. | `SneakyIsLostWhenTheCritterDealsDamage`, `SneakyIsNotLostByTakingDamage` |
| R17 | Sneaky attacker vs the counterattack | Takes it. Sneaky prevents being *declared* a target, not the exchange it started. | `SneakyAttackerTakesTheCounterattack` |
| R18 | Sneaky vs untargeted sweeps | No protection; it blocks *targeting*. | `BoardSweepHitsSneakyCritters` |
| R19 | Sneaky vs friendly targeting | No protection — the keyword says "by the enemy". | `FriendlyEffectMayTargetOwnSneakyCritter` |
| R23 | Zoomies on an effect-summoned critter | May attack that turn. | `EffectSummonedZoomiesCritterMayAttackThisTurn` |
| — | `Hello:` on a summon | Does not fire. It is "played from hand". | `HelloDoesNotFireForASummonedCopy` |
| — | `Goodbye:` source | Reads its critter as it was at the moment it died. | `GoodbyeSeesItsOwnStatsAtTheMomentOfDeath` |

## Zones, the hand and the board

| # | Question | Ruling | Test |
|---|---|---|---|
| R24 | Board full | Playing a critter is refused `BoardFull`; a summon **effect** fizzles with `SummonFizzled`. | `PlayingACritterIntoAFullBoardIsRefusedBoardFull`, `SummonIntoAFullBoardFizzlesWithAnEvent` |
| R25 | Hand full | Every add-to-hand, not only a draw, sends the card to the bottom of the owner's deck. | `BounceIntoAFullHandGoesToTheDeckBottom` |
| R25a | Does a non-draw overflow tick Tuckered Out? | **No**. The clock is about draws a seat could not take; a bounce is not a draw. | `BounceIntoAFullHandDoesNotTickTuckeredOut` |
| R40 | Does a bounced card re-enter the unseen pool? | No. The pool shrinks monotonically. | `BouncedCardStaysOutOfTheUnseenPool` |
| R41 | Controller change | Never; a critter's owner is fixed. | — (no primitive can change it) |
| R44 | Does buffing max health heal? | No; damage is stored separately. | `MaxHealthBuffDoesNotRestoreHealth` |

## The clock

| # | Question | Ruling | Test |
|---|---|---|---|
| R26 | A draw the hand refused | Ticks Tuckered Out, and the card still goes to the bottom. Closes the stall in game-design.md's own "no stall-outs". | `DrawIntoAFullHandTicksTuckeredOut`, `OverflowedCardStillReachesTheDeckBottom` |
| R27 | Tuckered Out on effect draws | Yes — every unsatisfiable draw. | `DrawEffectOnAnEmptyDeckTicksTuckeredOut` |
| R28 | The counter | Per seat, monotonic, never resets. | `TuckeredOutCounterNeverResets`, `TuckeredOutCountersAreIndependentPerSeat` |
| — | Empty deck **and** full hand | One refused draw is one tick, however many ways it was refused. | `EmptyDeckAndFullHandTicksExactlyOnce` |
| — | Visibility | Public. | `TuckeredOutTicksArePublic` |

## Mana

| # | Question | Ruling | Test |
|---|---|---|---|
| R29 | Starting max mana | 0; the turn-1 ramp produces 1. | `FirstTurnHasExactlyOneMana` |
| R30 | Unspent mana | Does not carry; the refill sets current to maximum. | `UnspentManaDoesNotCarryOver` |
| R31 | The Acorn | +1 **current** mana for the turn; current may exceed maximum. | `TheAcornGrantsOneCurrentManaOnly`, `CurrentManaMayExceedMaxAfterTheAcorn` |
| R31a | Permanent mana gain | Raises the **maximum only**. "Gain +1 max mana permanently" is a ramp payable from the next refill, not a ramp plus an acorn today. | `PermanentManaRaisesTheMaximumWithoutGrantingItNow` |
| R31b | Harvest Moon's extra ramp | Arrives as a turn-start effect, so it raises the maximum after that turn's refill: current lags maximum by one from the turn it first fires. | `HarvestMoonsExtraRampIsUsableFromTheNextTurn` |
| R49 | Acorn Rain's "first trick" | A per-seat, per-turn counter reset in the start-of-turn package. | `AcornRainDiscountsOnlyTheFirstTrickEachTurn`, `AcornRainCounterResetsAtStartOfTurn` |
| — | Cost floor | Never below zero. | `DiscountedCostFloorsAtZero` |

## The deal and the mulligan

| # | Question | Ruling | Test |
|---|---|---|---|
| R32 | Does the first seat draw on turn 1? | Yes, both seats draw at the start of their own first turn. | `BothSeatsDrawOnTheirOwnFirstTurn` |
| R33 | Is The Acorn mulliganable? | Moot by construction: it is granted when the mulligan ends, so it is never in a hand the step is asked about. It is public from the grant and never in the unseen pool. | `TheAcornIsGrantedWhenTheMulliganResolves`, `TheAcornIsPublicWhenGrantedAndNotInTheUnseenPool` |
| R34 | Can a mulligan redraw a card just returned? | Yes — the deck is reshuffled before the redraw. | `ARedrawnCardMayBeOneJustReturned` |
| R35 | When does a seat's mulligan resolve? | When it is submitted; the arrival order is on the timeline, so every replay reproduces it. | `EachSeatsMulliganResolvesWhenSubmitted`, `TheOrderTheSeatsAnswerInIsReplayedExactly` |
| R36 | A second mulligan submission | `AlreadyMulliganed`. | `SecondMulliganIsRefusedAsAlreadyMulliganed` |
| R50 | The first-seat decision | Drawn after both shuffles, from the same stream, and public. | `DealConsumesTheStreamInTheFixedOrder` |
| — | Instance identities | Minted in authored order before the shuffle, never in draw order. | `InstanceIdsAreMintedInAuthoredOrderNotDrawOrder` + its negative control |

## Resolution, winning and losing

| # | Question | Ruling | Test |
|---|---|---|---|
| R37 | When is the Den checked? | Once per resolution, after the queue drains. A Den healed back above zero in the same resolution survives. | `DenHealedBackInsideTheSameResolutionSurvives`, `PicnicDayHealCanPreventLethalInTheSameResolution` |
| R38 | Both Dens at zero | Draw. | `BothDensAtZeroInOneResolutionIsADraw`, `DrawResultNamesNoWinner` |
| R39 | When are deaths checked? | Between queue items — state-based, repeated until stable. | `EngineRemovesDeadCrittersBetweenQueueItems`, `ChainedGoodbyesResolveAgainstAnUpdatedBoard` |
| R39a | Weather vs Goodbye on a multi-death sweep | Per death, in canonical death order: Weather then that critter's Goodbye, then the next death's pair. Not all Weathers followed by all Goodbyes. | `WeatherAndGoodbyeInterleavePerDeath` |
| R45 | Heist eligibility | Cards played **from hand** whose instance came from that seat's own starting deck. Tokens, summons and graveyard copies are excluded. | `ResultCarriesEachSeatsPlayedCards`, `SummonedTokensAreNotHeistEligible`, `CopiedEnemyTrickIsNotHeistEligible` |
| R46 | Concede | Not an engine intent. Occupancy is the actor's. | — (no such intent exists) |
| R47 | Effect-queue termination | A drain is capped at 256 items; exceeding it is a failure, not a policy. | `SelfReenqueueingEffectHitsTheDrainCapAndFails` |
| — | Damage that landed on nothing | Not damage dealt: it feeds no Snacktime and reveals no Sneaky. | `DamageThatLandedOnNothingFeedsNoSnacktime` |

## Ranks and Weather

| # | Question | Ruling | Test |
|---|---|---|---|
| R43 | Rank-track deltas | Cumulative over every threshold at or below the card's rank. | `RankFiveEmberKitGainsOnePointInEach`, `RankFiveOldMossbackGainsSixPointsInEach` |
| R43a | What a track scales | The step's **primary** amount only, never its second (a Buff's health delta, a Peek's keep count). | `ARankTrackScalesOnlyTheStepsPrimaryAmount` |
| R48 | Weather symmetry | Exactly one per match, applied identically to both seats. | `WeatherAppliesIdenticallyToBothSeats`, `ExactlyOneWeatherIsInForce` |
| — | The Weather aura | Applied when a critter enters play, not re-queried. A Bubble Bath Bubble stays popped once it has absorbed something; an aura that re-granted it would be an unbreakable shell. | `BubbleBathGrantsBubbleToEveryCritter` |
| — | Adding a Weather | Requires no engine change. | `AConfigOnlyWeatherWorksWithNoEngineBranch` |

## The one interactive resolution

| # | Question | Ruling | Test |
|---|---|---|---|
| — | What the pause holds | The queue, for the acting seat's choice. Nobody else may act; the queue is held, not abandoned. | `NoIntentIsAcceptedMidResolution`, `TheQueueResumesAfterTheChoice` |
| — | The default | Keep the highest-cost cards, ties on `CardInfo.CompareCanonical`. | `LapsedChoiceDeadlineKeepsTheHighestCostCard`, `DefaultChoiceBreaksTiesOnCanonicalCardOrder` |
| — | The turn clock across the pause | The displaced deadline comes back at exactly the stamp it had. The pause is the seat's own thinking time: it grants no free turn time and re-arming a fresh one would hand out a second turn. | `APeekDoesNotHandTheSeatAFreshTurnDeadline` |
| — | No choice deadline configured | The held resolution inherits whatever deadline was in force, so a host that armed turn deadlines but no choice deadline cannot stall the table. | `AZeroChoiceDeadlineInheritsTheTurnDeadline` |
| — | An empty deck | A no-op, not a pause. | `PeekOnAnEmptyDeckIsANoOpRatherThanAPause` |

## Reasoning

**A non-draw overflow does not tick Tuckered Out (R25a).** game-design.md's clock is stated as "when a draw cannot be
taken", and a bounce is not a draw; ticking on one would let an opponent's Undertow advance your clock. Draws —
including effect draws — tick, which is what closes the stall.

**Permanent mana raises the maximum only (R31a, R31b).** Granting current mana as well makes a 2-mana card refund half
of itself the turn it is played. Harvest Moon's ramp is a turn-start *effect*, which resolves after the refill, so the
extra maximum is usable from the next turn. Special-casing the Weather to dodge that would be the branch on a Weather's
identity the whole decomposition exists to avoid.

**Weather and Goodbye interleave per death (R39a).** effects.md says the Weather goes first when a Weather and cards
trigger on the same event. A sweep that kills three critters is three deaths, not one event, so the queue reads
Weather-then-Goodbye three times. The ordering is pinned because an unpinned one is a determinism bug waiting for a
Goodbye that matters.

**A rank track scales one number (R43a).** Applying the delta to a step's second amount as well would turn "restore
one more" into "look at one more" on any card that had both.

**The turn clock across a peek.** A held peek displaces the turn deadline, and the displaced deadline comes back at
exactly the stamp it had. Re-arming a fresh one when the choice lands would hand the seat a second turn's worth of time
for casting one card; the pause is the seat's own thinking time.

**A zero choice deadline inherits the turn deadline.** Otherwise a host that armed turn deadlines but no choice
deadline would leave a held peek with no clock at all, and one silent seat could stall the table.

## See also

- [`game-design.md`](game-design.md) — the game these rulings fill the gaps in.
- [`rules.md`](rules.md) — why the rules are shaped the way they are.
- [`effects.md`](effects.md) — the effect vocabulary and its resolution semantics.
