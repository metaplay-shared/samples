using Game.Logic;
using Metaplay.Client;
using Metaplay.Core.Client;
using Metaplay.Core.Session;
using Metaplay.Unity;
using WebClient.Gameplay;
using WebClientBase.Services;

namespace WebClient.Services;

/// <summary>
/// Game-specific Metaplay client service that manages the Metaplay client connection. Inherits connection
/// lifecycle management from <see cref="MetaplayClientServiceBase{TModel}"/> and implements the player model
/// client listener so model changes re-render the UI.
/// </summary>
public class MetaplayClientService : MetaplayClientServiceBase<PlayerModel>, IPlayerModelClientListener
{
    /// <summary>Create the Metaplay client.</summary>
    protected override IMetaplayClient CreateClient()
    {
        return MetaplayClient.Create();
    }

    /// <summary>
    /// Raised when a new attempt is ready to play, with the field it is raced against, or null for none.
    /// </summary>
    public event Action<TimeTrialTicket, IReadOnlyList<TimeTrialGhost>?>? TimeTrialStarted;

    /// <summary>
    /// Raised when the server has run a submitted recording and recorded the time it computed. The standing
    /// is where the run left the player on the course, or null if the course's pool could not be reached.
    /// </summary>
    public event Action<TimeTrialResult, TimeTrialStanding?>? TimeTrialAccepted;

    /// <summary>Raised when the server would not count a submitted recording.</summary>
    public event Action<int, TimeTrialRejectReason>? TimeTrialRejected;

    /// <summary>
    /// Whether the session is running against the in-process offline server. Whether a run counts is the
    /// ticket's to say (<see cref="TimeTrialTicket.IsPractice"/>), not this.
    /// </summary>
    public bool IsOfflineMode => IEnvironmentConfigProvider.Get().ConnectionEndpointConfig.IsOfflineMode;

    /// <summary>
    /// Whether this session may use the developer course tools: offline mode, whose in-process server runs
    /// developer-only actions, or an online session whose player is a developer or whose server runs with
    /// its development features on. This decides only whether the tools are shown; the server's refusal of
    /// a developer-only request is what gates them.
    /// </summary>
    public bool AllowsDeveloperTools => IsOfflineMode || (PlayerModel?.AllowsDeveloperActions ?? false);

    /// <summary>
    /// Whether the player has completed a run. Offline, where no run is ever accepted, a run finished in this
    /// visit counts (<see cref="MarkOfflineRunFinished"/>), and a reload forgets it.
    /// </summary>
    public bool HasCompletedARun => (PlayerModel?.HasCompletedARun ?? false) || (IsOfflineMode && _offlineRunFinished);

    bool _offlineRunFinished;

    /// <summary>An offline run has reached the finish line: from now on this visit counts as having completed a run.</summary>
    public void MarkOfflineRunFinished()
    {
        if (!IsOfflineMode || _offlineRunFinished)
            return;

        _offlineRunFinished = true;
        NotifyStateChanged();
    }

    /// <summary>
    /// A run the developer tools asked for, waiting for the run screen to open it: a course by id from the
    /// course tools, or a draft by value from the part editor. <paramref name="ReturnPath"/> is the page the run
    /// goes back to when it is left. <paramref name="AltitudeBand"/> is the band a draft is drawn in, and null
    /// for a course.
    /// </summary>
    public sealed record DeveloperRunRequest(CourseId? CourseId, DraftCoursePart? DraftPart, bool Race, string ReturnPath, string? AltitudeBand = null);

    DeveloperRunRequest? _pendingDeveloperRun;

    /// <summary>Raised when a developer run has been asked for and is waiting to be opened (<see cref="TakePendingDeveloperRun"/>).</summary>
    public event Action? DeveloperRunQueued;

    /// <summary>
    /// The course of a developer race whose field is driven here, held until its ticket arrives.
    /// </summary>
    CourseId? _browserFieldCourseId;

    /// <summary>
    /// What the developer course tools last had selected, so returning to them from a run lands on the same
    /// course. Presentation only.
    /// </summary>
    public string? DeveloperToolsSelection { get; set; }

    /// <summary>
    /// The part editor's draft, the editor's own object, so returning to the editor from a run finds the
    /// draft as it was left. Presentation only: nothing reads it but the editor.
    /// </summary>
    public CoursePartDraft? DeveloperPartDraft { get; set; }

    /// <summary>
    /// Hand a practice or race on <paramref name="courseId"/> to the run screen, which opens it behind its
    /// curtain the way it opens any run. The request waits here because the run screen is another page.
    /// Leaving the run goes back to the course tools.
    /// </summary>
    public void RequestDeveloperRun(CourseId courseId, bool race)
        => QueueDeveloperRun(new DeveloperRunRequest(courseId, null, race, "dev/courses"));

    /// <summary>
    /// Hand a practice or race on the part editor's <paramref name="draft"/>, composed alone, to the run screen,
    /// drawn in <paramref name="altitudeBand"/>. The draft travels by value because it is in no published
    /// config; leaving the run goes back to the editor, which restores the draft from
    /// <see cref="DeveloperPartDraft"/>.
    /// </summary>
    public void RequestDraftPartRun(DraftCoursePart draft, bool race, string altitudeBand)
        => QueueDeveloperRun(new DeveloperRunRequest(null, draft, race, "dev/parts", altitudeBand));

    void QueueDeveloperRun(DeveloperRunRequest request)
    {
        _pendingDeveloperRun = request;
        DeveloperRunQueued?.Invoke();
    }

    /// <summary>The developer run waiting to be opened, taken so it is opened once, or null for none.</summary>
    public DeveloperRunRequest? TakePendingDeveloperRun()
    {
        DeveloperRunRequest? request = _pendingDeveloperRun;
        _pendingDeveloperRun = null;
        return request;
    }

    /// <summary>
    /// Open a developer's practice or race on a course they chose. The ticket is a practice ticket either way,
    /// so nothing about the run is recorded.
    /// </summary>
    void StartDeveloperTimeTrial(CourseId courseId, bool race)
    {
        _browserFieldCourseId = null;

        // Online, a race asks the server, which selects the field the course would give a real run. The
        // in-process offline server has no pool to ask, so there a race is the practice action with its field
        // driven here.
        if (!race || IsOfflineMode)
        {
            if (race)
                _browserFieldCourseId = courseId;
            ExecuteAction(new PlayerStartPracticeTimeTrial(courseId));
            return;
        }

        MetaplaySDK.RunOnMainThreadAsync(() =>
        {
            Session?.PlayerContext?.FlushActions();
            MetaplaySDK.MessageDispatcher.SendMessage(new TimeTrialStartOnMapRequest(courseId));
        });
    }

    /// <summary>
    /// Open a developer's practice or race on a part editor draft, composed alone. The ticket is a practice
    /// ticket either way.
    /// </summary>
    void StartDraftPartTimeTrial(DraftCoursePart draft, bool race)
    {
        // A draft has no pool in any mode, so a race on it has its field driven here.
        _browserFieldCourseId = race && draft.Id != null ? CourseId.FromString(draft.Id.Value) : null;
        ExecuteAction(new PlayerStartDraftPartTimeTrial(draft));
    }

    /// <summary>Open the run a <see cref="DeveloperRunRequest"/> asks for, whichever kind it is.</summary>
    public void StartDeveloperTimeTrial(DeveloperRunRequest request)
    {
        if (request.DraftPart != null)
            StartDraftPartTimeTrial(request.DraftPart, request.Race);
        else if (request.CourseId != null)
            StartDeveloperTimeTrial(request.CourseId, request.Race);
    }

    /// <summary>
    /// The published config this session is playing on, or null before a session has started. The movement
    /// values and the Skill definitions both come out of it, so the simulation the page drives is the one the
    /// server will run again.
    /// </summary>
    public SharedGameConfig? GameConfig => PlayerModel?.GameConfig;

    /// <summary>
    /// Ask for a run. The ticket arrives through <see cref="TimeTrialStarted"/>.
    /// </summary>
    public void StartTimeTrial()
    {
        // Online, the server picks the map from the registry's active set and answers with the ticket and the
        // field together. Offline there is no registry to ask, so the run is a practice on the requested course
        // or the published offline practice map, raced against nobody.
        if (IsOfflineMode)
        {
            CourseId? courseId = OfflineCourseOverride.RequestedCourseId ?? GameConfig?.Global.OfflinePracticeMapId;
            if (courseId != null)
                ExecuteAction(new PlayerStartPracticeTimeTrial(courseId));
            return;
        }

        MetaplaySDK.RunOnMainThreadAsync(() =>
        {
            // Actions are batched, and this message is not. Flushing first keeps the request behind everything
            // the player has already done, so the ticket it opens is issued against that state.
            Session?.PlayerContext?.FlushActions();
            MetaplaySDK.MessageDispatcher.SendMessage(new TimeTrialStartRequest());
        });
    }

    /// <summary>
    /// Buy a Skill with Orbs. The action is checked against the same model state and published config on
    /// the server, so the page may offer whatever the model says is affordable and let the action be the
    /// authority on whether it happens.
    /// </summary>
    public void UnlockSkill(SkillId skillId) => ExecuteAction(new PlayerUnlockSkill(skillId));

    /// <summary>Put an owned Skill in the player's one slot, or empty the slot with a null id.</summary>
    public void EquipSkill(SkillId? skillId) => ExecuteAction(new PlayerEquipSkill(skillId));

    /// <summary>
    /// Set the name the player's ghost is known by. Only a Name Tag owner may; the action trims the name and
    /// checks it with <see cref="PlayerModel.IsValidPlayerName"/>, which the page can run on the input first.
    /// </summary>
    public void SetPlayerName(string name) => ExecuteAction(new PlayerSetName(name));

    /// <summary>
    /// Hand a finished run to the server. The server runs the recording through its own simulation and answers
    /// with <see cref="PlayerAcceptTimeTrialResult"/> or <see cref="PlayerRejectTimeTrialResult"/>, which
    /// arrive on the model and through the listener hooks below.
    /// </summary>
    public void SubmitTimeTrial(int ticketId, TimeTrialRecording recording)
    {
        MetaplaySDK.RunOnMainThreadAsync(() =>
        {
            // Actions are batched, and this message is not. Without the flush, a submission can reach the
            // server ahead of the action that opened the ticket it names, and be turned down for an attempt
            // the server has not heard of yet.
            Session?.PlayerContext?.FlushActions();
            MetaplaySDK.MessageDispatcher.SendMessage(new TimeTrialSubmitRequest(ticketId, recording));
        });
    }

    /// <summary>
    /// Called when a session has started. Wire up the client listener so model changes re-render the UI.
    /// </summary>
    protected override void OnSessionStarted(MetaplaySession session)
    {
        PlayerModel? playerModel = session.PlayerContext.Model as PlayerModel;
        if (playerModel != null)
            playerModel.ClientListener = this;
    }

    #region Model client listeners (trigger UI re-render)

    void IPlayerModelClientListener.OnTimeTrialStarted(TimeTrialTicket ticket, IReadOnlyList<TimeTrialGhost> lineup)
    {
        // A developer race whose field is driven here: its ticket came from an action that fields nobody, so the
        // course's scripted runners stand in, the same field the server falls back to. The course comes off the
        // ticket, which is what carries a draft.
        CourseId? raceCourseId = _browserFieldCourseId;
        _browserFieldCourseId = null;
        if (lineup == null && ticket.IsPractice && raceCourseId != null && ticket.CourseId == raceCourseId && GameConfig != null)
        {
            CourseDefinition? course = CourseResolver.TryResolve(ticket, GameConfig);
            if (course != null)
                lineup = TimeTrialSeedRunners.RunField(HasCompletedARun, null, course, GameConfig.TimeTrialRules);
        }

        TimeTrialStarted?.Invoke(ticket, lineup);
        NotifyStateChanged();
    }

    void IPlayerModelClientListener.OnTimeTrialAccepted(CourseId courseId, TimeTrialResult result, bool isNewBest, TimeTrialStanding standing)
    {
        TimeTrialAccepted?.Invoke(result, standing);
        NotifyStateChanged();
    }

    void IPlayerModelClientListener.OnTimeTrialRejected(int ticketId, TimeTrialRejectReason reason)
    {
        TimeTrialRejected?.Invoke(ticketId, reason);
        NotifyStateChanged();
    }

    #endregion
}
