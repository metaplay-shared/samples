using Metaplay.Core.Config;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// The content of one weekly themed event: its name, its target and its reward (<c>docs/weekly-event.md</c>).
    /// The SDK copies the content into the event when the event is created, so a config publish cannot change a
    /// running week. The scoring rule is code in <see cref="WeeklyEventScoring"/>.
    /// </summary>
    /// <remarks>
    /// Every setter must stay private. Match-completion consumers receive this object by reference
    /// (<see cref="LiveOpsEventSnapshot.Content"/>) in an unsynchronized server action, and it is part of the
    /// checksummed <c>PlayerModelBase.LiveOpsEvents</c>, so a write would cause a checksum mismatch.
    /// </remarks>
    [LiveOpsEvent(100, "Weekly Themed Event")]
    public class WeeklyEventContent : LiveOpsEventContent
    {
        /// <summary>The player-facing name of the week, for example "Trickster's Week".</summary>
        [MetaMember(1)] public string Theme { get; private set; }

        /// <summary>A one-line description shown under <see cref="Theme"/>.</summary>
        [MetaMember(2)] public string Tagline { get; private set; }

        /// <summary>The points needed to make <see cref="Reward"/> claimable.</summary>
        [MetaMember(3)] public int TargetPoints { get; private set; }

        /// <summary>The bonus points for winning a table, added to the points for tricks taken.</summary>
        [MetaMember(4)] public int WinBonusPoints { get; private set; }

        /// <summary>The event's only reward. The player must claim it. The event never grants it automatically.</summary>
        [MetaMember(5)] public RewardBundle Reward { get; private set; }

        /// <summary>
        /// True, which is the SDK default, stated explicitly: a player who leaves the target audience during
        /// the week keeps the event and the progress in it.
        /// </summary>
        public override bool AudienceMembershipIsSticky => true;

        public WeeklyEventContent() { }

        public WeeklyEventContent(string theme, string tagline, int targetPoints, int winBonusPoints, RewardBundle reward)
        {
            Theme          = theme;
            Tagline        = tagline;
            TargetPoints   = targetPoints;
            WinBonusPoints = winBonusPoints;
            Reward         = reward;
        }

        /// <summary>
        /// Creates the per-player <see cref="WeeklyEventPlayerModel"/>, which holds no state of its own. The
        /// model is created here rather than by the SDK's reflection fallback so the content-to-model mapping is
        /// explicit.
        /// </summary>
        public override PlayerLiveOpsEventModel CreateModel(PlayerLiveOpsEventInfo info) => new WeeklyEventPlayerModel(info);

        /// <summary>
        /// Called by the SDK when an event is created from this content, whether or not it came from a template.
        /// <para>
        /// Runs only <see cref="WeeklyEventScoring.ProblemsWith"/>, not
        /// <see cref="WeeklyEventScoring.BalanceProblemsWith"/>, because a target one game can reach makes an easy
        /// week, not a broken one, and the live-server tests create such events on purpose. The config build runs
        /// both on templates (<see cref="WeeklyEventTemplateInfo.Validate"/>).
        /// </para>
        /// </summary>
        public override void Validate(ILiveOpsEventValidationLog log, FullGameConfig activeGameConfig)
        {
            foreach (string problem in WeeklyEventScoring.ProblemsWith(this))
                log.Error(problem);
        }

        public override string ToString() => $"{Theme}: {TargetPoints} points for {Reward}";
    }

    /// <summary>
    /// One weekly event template that a week's event can be created from.
    /// <para>
    /// It subclasses the SDK's template type because that type has no public constructor, and to implement
    /// <see cref="IValidatedConfigItem"/>.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class WeeklyEventTemplateInfo : LiveOpsEventTemplateConfigData<WeeklyEventContent>, IValidatedConfigItem
    {
        public WeeklyEventTemplateInfo() { }

        public WeeklyEventTemplateInfo(LiveOpsEventTemplateId templateId, WeeklyEventContent content)
        {
            TemplateId = templateId;
            Content    = content;
        }

        /// <summary>
        /// Validates the template with both <see cref="WeeklyEventScoring.ProblemsWith"/> and
        /// <see cref="WeeklyEventScoring.BalanceProblemsWith"/>, and validates the reward.
        /// <para>
        /// <see cref="WeeklyEventContent.Validate"/>, which the SDK runs at event creation, skips the balance
        /// checks. The reward against the wallet caps in <c>Global</c> is checked in
        /// <see cref="GameConfigValidation"/>.
        /// </para>
        /// </summary>
        public void Validate(ConfigItemValidation validation)
        {
            if (Content == null)
            {
                validation.Error("has no content", nameof(Content));
                return;
            }

            foreach (string problem in WeeklyEventScoring.ProblemsWith(Content))
                validation.Error(problem, nameof(Content));

            foreach (string problem in WeeklyEventScoring.BalanceProblemsWith(Content))
                validation.Error(problem, nameof(Content));

            Content.Reward?.Validate(validation, nameof(Content));
        }

        public override string ToString() => $"{TemplateId}: {Content}";
    }
}
