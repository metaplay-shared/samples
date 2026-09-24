using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// One primitive invocation waiting to resolve. The queue is a flat FIFO: an action enqueues its steps,
    /// each resolves against the state as it is when dequeued, and anything a step causes enqueues at the tail
    /// (<c>Docs/effects.md</c>). There are no priority windows and no reactions.
    /// </summary>
    [MetaSerializable]
    public class EffectQueueItem
    {
        [MetaMember(1)] public MetaRef<EffectStepInfo> Step { get; private set; }
        /// <summary>
        /// The seat the step resolves as: the card's controller, or for a Weather step the seat the event
        /// belongs to. Every side-relative target kind is relative to this.
        /// </summary>
        [MetaMember(2)] public int                     ResolvingSeat { get; private set; }
        /// <summary> The source as it was when the trigger was enqueued, so a Goodbye reads its critter at death. </summary>
        [MetaMember(3)] public CardInstanceSnapshot    Source { get; private set; }
        /// <summary> The one target the player picked when playing the card, or none. </summary>
        [MetaMember(4)] public EffectTargetRef         ChosenTarget { get; private set; }
        /// <summary>
        /// What the source card's rank track adds to the literal base of this step's amount. Non-zero only on
        /// the first step of a card's Hello, which is where a track's amount delta applies.
        /// </summary>
        [MetaMember(5)] public int                     AmountBonus { get; private set; }
        /// <summary>
        /// True on a played card's own Hello. The card is on its seat's played list from the moment it was
        /// played, so a counter that scales on "cards you have played this match" must leave itself out —
        /// which is one flag here rather than a special case inside the counter.
        /// </summary>
        [MetaMember(7)] public bool                    ExcludesSourceFromPlayedCount { get; private set; }

        public EffectQueueItem() { }

        public EffectQueueItem(MetaRef<EffectStepInfo> step, int resolvingSeat, CardInstanceSnapshot source, EffectTargetRef chosenTarget, int amountBonus, bool excludesSourceFromPlayedCount)
        {
            Step          = step;
            ResolvingSeat = resolvingSeat;
            Source        = source;
            ChosenTarget  = chosenTarget;
            AmountBonus   = amountBonus;
            ExcludesSourceFromPlayedCount = excludesSourceFromPlayedCount;
        }

        public EffectStepInfo Info => Step.Ref;

        public override string ToString() => $"{Step?.KeyObject} as seat{ResolvingSeat}";
    }
}
