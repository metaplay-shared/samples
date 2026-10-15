using Game.Logic;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WebClient.Gameplay;

/// <summary>
/// How far one attempt has got. A run that is played offline stops at <see cref="Finished"/>: there is no
/// server to hand it to, so its time stays the browser's own.
/// </summary>
public enum TimeTrialPhase
{
    /// <summary>The ticket is in hand and the first animation frame has not arrived yet.</summary>
    Ready,

    /// <summary>Being played.</summary>
    Running,

    /// <summary>Over, showing the time the browser's own simulation computed. Nothing is recorded yet.</summary>
    Finished,

    /// <summary>Handed to the server, waiting for it to run the recording for itself.</summary>
    Validating,

    /// <summary>The server ran the recording and recorded the time it computed.</summary>
    Accepted,

    /// <summary>The server would not count the run; <see cref="TimeTrial.RejectReason"/> says why.</summary>
    Rejected,
}

/// <summary>
/// One attempt as the browser plays it: the ticket it was opened with, the simulation it is played in, the
/// inputs it records for the server, and where the attempt has got to.
/// <para>
/// The run is driven from the animation frame loop in <c>wwwroot/js/timeTrialHost.js</c>, which calls
/// <see cref="Frame"/> with the page's timestamp and draws what comes back. This class turns that wall clock
/// into whole simulation steps: everything the player does goes through <see cref="TimeTrialSimulation.ApplyInput"/>
/// and <see cref="TimeTrialSimulation.Step"/> in the same order the server will replay it in, which is what
/// makes the server's own run of the recording land on the same time.
/// </para>
/// </summary>
public sealed class TimeTrial
{
    /// <summary>
    /// The most steps one frame may catch up on. A tab that was switched away from comes back with an
    /// arbitrarily long gap on the wall clock; running all of it would freeze the page and hand the player a
    /// stretch of course they never saw. The surplus is dropped instead, so the run resumes where it was.
    /// </summary>
    const int MaxCatchUpSteps = 6;

    static readonly double StepDurationMs = 1000.0 / TimeTrialConstants.StepsPerSecond;

    /// <summary>
    /// How long the runner is watched running on past the finish line, under no input, before the score
    /// screen's curtain starts. The time is taken at the crossing; this is only how long the page lingers.
    /// </summary>
    public const int RunOutMs = 1600;

    /// <summary>
    /// The longest the run-out is drawn for, in steps. It normally ends at <see cref="EndRunOut"/>, once the
    /// curtain has covered it; this is the backstop that ends the frame loop when nothing covers it.
    /// </summary>
    const int RunOutLimitSteps = TimeTrialConstants.StepsPerSecond * 4;

    /// <summary>
    /// How long the run-in before the start line lasts, in simulation steps of wall clock. The run-in is drawn
    /// only. The simulation, the recording and the clock all start at step 0 on the start line.
    /// </summary>
    public const int RunInSteps = TimeTrialConstants.StepsPerSecond * 3 / 2;

    readonly TimeTrialSimulation _simulation;
    readonly TimeTrialRules      _rules;
    readonly TimeTrialRecording  _recording = new TimeTrialRecording();

    /// <summary>The opponents being reproduced alongside the run, fastest expected finish first.</summary>
    readonly List<GhostPlayback> _ghosts = new List<GhostPlayback>();

    /// <summary>Their simulations, in the same order, so a frame's snapshot needs no allocation of its own.</summary>
    readonly List<TimeTrialSimulation> _ghostSimulations = new List<TimeTrialSimulation>();

    /// <summary>Why each of them is off screen for the moment, or that it is not, in the same order.</summary>
    readonly List<GhostHideReason> _ghostHideReasons = new List<GhostHideReason>();

    /// <summary>
    /// The whole field the server put in front of this attempt, including any opponent whose recording the
    /// browser cannot reproduce. A segment is ranked against all of them, the way the server ranks it.
    /// </summary>
    readonly List<TimeTrialGhost> _lineup = new List<TimeTrialGhost>();

    /// <summary>
    /// Whether the lineup can be compared segment by segment: it has opponents, and every one of them timed
    /// the same torii this course has. Practice alone has no lineup and is never placed.
    /// </summary>
    readonly bool _lineupPlaceable;

    /// <summary>Where the run came in on each segment it has closed, in course order.</summary>
    readonly List<int> _segmentPlacements = new List<int>();

    /// <summary>Button transitions that have happened since the last step, applied one per step in order.</summary>
    readonly Queue<bool> _pendingTransitions = new Queue<bool>();

    double _previousTimestampMs = double.NaN;
    double _unsteppedMs;

    /// <summary>The state the last accepted transition left the button in, so only real edges are recorded.</summary>
    bool _buttonDown;

    bool _finishReported;

    /// <summary>Simulation steps' worth of wall clock the run-in still has to play before step 0 is simulated.</summary>
    int _runInStepsLeft = RunInSteps;

    /// <summary>Simulation steps' worth of wall clock that have passed since the runner crossed the finish line.</summary>
    int _runOutSteps;

    /// <summary>Set once the run-out has been covered and nothing more needs drawing.</summary>
    bool _runOutEnded;

    /// <summary>The attempt this run was opened with, and the only one its recording can be counted against.</summary>
    public TimeTrialTicket Ticket { get; }

    public CourseDefinition Course { get; }

    /// <summary>
    /// True when the ticket is a practice ticket: the server ranks the run nowhere and pays nothing for it,
    /// whatever field it is raced against.
    /// </summary>
    public bool IsPractice => Ticket.IsPractice;

    public TimeTrialPhase Phase { get; private set; } = TimeTrialPhase.Ready;

    /// <summary>What the browser's own simulation made of the run, or null until it reaches the finish line.</summary>
    public TimeTrialResult ProvisionalResult { get; private set; }

    /// <summary>What the server made of the recording, or null until it has answered.</summary>
    public TimeTrialResult AcceptedResult { get; private set; }

    /// <summary>
    /// Where the accepted run left the player among everyone who has run this course, or null while the run
    /// is unfinished and when the server had no standing to give.
    /// </summary>
    public TimeTrialStanding? Standing { get; private set; }

    /// <summary>Why the server would not count the run, once <see cref="Phase"/> is <see cref="TimeTrialPhase.Rejected"/>.</summary>
    public TimeTrialRejectReason RejectReason { get; private set; }

    /// <summary>Everything the player pressed, in the order the server will apply it.</summary>
    public TimeTrialRecording Recording => _recording;

    /// <summary>How many opponents the browser can reproduce on this course and runs against.</summary>
    public int GhostCount => _ghosts.Count;

    /// <summary>The rules the run is simulated by: the published rules with the ticket's Skill applied.</summary>
    public TimeTrialRules Rules => _rules;

    /// <summary>The rules each opponent being run against is simulated by, in the order the renderer draws them.</summary>
    public IReadOnlyList<TimeTrialRules> GhostRules
    {
        get
        {
            List<TimeTrialRules> rules = new List<TimeTrialRules>();
            foreach (GhostPlayback ghost in _ghosts)
                rules.Add(ghost.Rules);
            return rules;
        }
    }

    /// <summary>The field this attempt is raced against, fastest expected finish first. Empty for practice alone.</summary>
    public IReadOnlyList<TimeTrialGhost> Lineup => _lineup;

    /// <summary>
    /// Where the run came in on each segment it has closed, in course order, by the browser's own comparison
    /// with the lineup's recorded torii times. The placements that pay are the ones the server computes from
    /// its own run of the recording.
    /// </summary>
    public IReadOnlyList<int> ProvisionalSegmentPlacements => _segmentPlacements;

    /// <summary>How many segments this course is raced in: one per climb, plus the one the finish line closes.</summary>
    public int SegmentCount => TimeTrialSegments.CountFor(Course.Climbs.Count);

    /// <summary>
    /// The run time at which <paramref name="segment"/> closed, in milliseconds: when the runner reached its
    /// climb's bottom torii, or crossed the finish line for the last segment. It is the simulation's own clock,
    /// the one the score screen writes the finish time in, so the last segment's is the run's finish time.
    /// Zero for a segment the run has not closed yet.
    /// </summary>
    public int SegmentCloseTimeMs(int segment)
    {
        IReadOnlyList<int> climbEntries = _simulation.ClimbEntryTimesMs;
        if (segment >= 0 && segment < climbEntries.Count)
            return climbEntries[segment];
        if (segment == SegmentCount - 1 && ProvisionalResult != null)
            return ProvisionalResult.FinishTimeMs;
        return 0;
    }

    /// <summary>
    /// True while the runner is running in to the start line, before the clock starts. The button does
    /// nothing, and nothing pressed is recorded.
    /// </summary>
    public bool IsRunningIn => _runInStepsLeft > 0;

    /// <summary>True while the button does nothing: during the run-in, and while a climb owns movement.</summary>
    public bool IsInputLocked => IsRunningIn || _simulation.IsInputLocked;

    /// <summary>Time on the runner's clock right now, in milliseconds.</summary>
    public int ElapsedMs => _simulation.ElapsedMs;

    /// <summary>
    /// Raised once, on the frame the run is over: the finish line has been crossed, or the step budget ran
    /// out. A finished run keeps drawing its run-out past the line after this.
    /// </summary>
    public event Action Finished;

    /// <summary>
    /// Raised on the frame a segment is placed (<see cref="ProvisionalSegmentPlacements"/> has grown): at a
    /// climb's bottom torii, and at the finish line for the last segment, just before <see cref="Finished"/>.
    /// A run with no lineup to place against places nothing and never raises it.
    /// </summary>
    public event Action SegmentPlaced;

    /// <param name="lineup">
    /// The opponents the attempt is raced against, fastest expected finish first. Every one the run can play is run:
    /// a payload that does not belong to this attempt, or that does not reach the finish line, is skipped
    /// rather than shown.
    /// </param>
    public TimeTrial(TimeTrialTicket ticket, CourseDefinition course, SharedGameConfig gameConfig, IReadOnlyList<TimeTrialGhost>? lineup = null)
    {
        Ticket      = ticket ?? throw new ArgumentNullException(nameof(ticket));
        Course      = course ?? throw new ArgumentNullException(nameof(course));

        if (gameConfig == null)
            throw new ArgumentNullException(nameof(gameConfig));

        // The run is played on the ticket's Skill, which is the one the server will replay the recording with.
        _rules      = TimeTrialSkills.EffectiveRules(gameConfig, ticket.SkillId);
        _simulation = new TimeTrialSimulation(course, _rules);

        // A practice run alone has no lineup. A practice run with one is played and placed against it for
        // display like a ranked run.
        if (lineup == null)
        {
            _lineupPlaceable = false;
            return;
        }

        foreach (TimeTrialGhost ghost in lineup)
        {
            if (ghost == null)
                continue;

            _lineup.Add(ghost);

            // Each opponent is reproduced under its own Skill, so a stored ghost runs as it ran rather than as
            // this player's loadout would have run it.
            TimeTrialRules ghostRules = TimeTrialSkills.EffectiveRules(gameConfig, ghost.SkillId);
            if (!ghost.IsPlayableFor(ticket, course, ghostRules))
                continue;

            GhostPlayback playback = new GhostPlayback(course, ghostRules, ghost.Recording.Inputs);
            _ghosts.Add(playback);
            _ghostSimulations.Add(playback.Simulation);
            _ghostHideReasons.Add(GhostHideReason.RunIn);
        }

        _lineupPlaceable = _lineup.Count > 0;
        foreach (TimeTrialGhost ghost in _lineup)
        {
            if (!TimeTrialSegments.Covers(ghost.GateTimesMs, ghost.ClimbEntryTimesMs, SegmentCount))
                _lineupPlaceable = false;
        }
    }

    /// <summary>
    /// Advance the run to <paramref name="timestampMs"/> and hand back what to draw, as the JSON of a
    /// <see cref="TimeTrialRenderSnapshot"/>. Past the finish line it hands back the run-out: the runner
    /// running on under no input, with the result already taken at the crossing. Returns null when the loop
    /// should stop without drawing again: a run that used its whole step budget without reaching the finish
    /// line, or one whose run-out is over.
    /// </summary>
    /// <param name="timestampMs">The animation frame's timestamp, in milliseconds.</param>
    /// <param name="diagnostics">
    /// Whether the snapshot carries the properties only the end-to-end tests read, which the frame loop asks
    /// for only on a page the tests opened.
    /// </param>
    // Serialized here with the generated serializer rather than returned as an object, because Blazor's interop
    // would serialize an object by reflection and takes no generated serializer of its own.
    [JSInvokable]
    public string Frame(double timestampMs, bool diagnostics)
    {
        if (_finishReported && (!_simulation.IsFinished || _runOutEnded || _runOutSteps >= RunOutLimitSteps))
            return null;

        Advance(timestampMs);

        // Reported on the frame the line is crossed, so the recording goes to the server at the crossing and
        // the run-out that follows is drawing alone.
        if (_simulation.IsOver && !_finishReported)
        {
            ReportFinished();
            if (!_simulation.IsFinished)
                return null;
        }

        return JsonSerializer.Serialize(CreateSnapshot(diagnostics), TimeTrialRenderJsonContext.Default.TimeTrialRenderSnapshot);
    }

    /// <summary>
    /// The run-out has been seen through: the curtain covers it. The next frame hands back nothing, which
    /// stops the loop.
    /// </summary>
    public void EndRunOut() => _runOutEnded = true;

    /// <summary>The button went down. Recorded against the next step the run simulates.</summary>
    [JSInvokable]
    public void Press() => QueueTransition(pressed: true);

    /// <summary>The button came up. Recorded against the next step the run simulates.</summary>
    [JSInvokable]
    public void Release() => QueueTransition(pressed: false);

    /// <summary>
    /// One frame's worth of drawing state. <paramref name="diagnostics"/> fills the properties only the
    /// end-to-end tests read.
    /// </summary>
    TimeTrialRenderSnapshot CreateSnapshot(bool diagnostics)
    {
        // Each opponent's simulation and hide reason are read together here, so both come from the same instant.
        for (int ndx = 0; ndx < _ghosts.Count; ndx++)
        {
            GhostPlayback ghost = _ghosts[ndx];
            _ghostSimulations[ndx] = ghost.Simulation;
            _ghostHideReasons[ndx] = IsRunningIn                  ? GhostHideReason.RunIn
                                   : ghost.Hidden                 ? GhostHideReason.Climb
                                   : ghost.Simulation.IsFinished ? GhostHideReason.Finish
                                   : GhostHideReason.None;
        }

        TimeTrialRunOutPose? playerRunOut = _simulation.IsFinished
            ? TimeTrialRunOutPose.After(_simulation, Course, _rules, _runOutSteps)
            : null;

        float runInSeconds = _runInStepsLeft / (float)TimeTrialConstants.StepsPerSecond;
        return new TimeTrialRenderSnapshot(_simulation, _ghostSimulations, _ghostHideReasons, playerRunOut, runInSeconds, diagnostics);
    }

    /// <summary>The recording has been handed to the server and its answer is outstanding.</summary>
    public void MarkValidating()
    {
        if (Phase == TimeTrialPhase.Finished)
            Phase = TimeTrialPhase.Validating;
    }

    /// <summary>The server ran the recording and recorded <paramref name="result"/>.</summary>
    public void MarkAccepted(TimeTrialResult result, TimeTrialStanding? standing)
    {
        AcceptedResult = result;
        Standing       = standing;
        Phase          = TimeTrialPhase.Accepted;
    }

    /// <summary>The server would not count the run.</summary>
    public void MarkRejected(TimeTrialRejectReason reason)
    {
        RejectReason = reason;
        Phase        = TimeTrialPhase.Rejected;
    }

    /// <summary>
    /// Run the whole simulation steps the wall clock has covered since the last frame, applying the button
    /// transitions that happened in between at the steps they land on.
    /// </summary>
    void Advance(double timestampMs)
    {
        Phase = Phase == TimeTrialPhase.Ready ? TimeTrialPhase.Running : Phase;

        // The first frame only establishes where the clock started; there is no elapsed time to run yet.
        if (double.IsNaN(_previousTimestampMs))
        {
            _previousTimestampMs = timestampMs;
            return;
        }

        double frameMs = timestampMs - _previousTimestampMs;
        _previousTimestampMs = timestampMs;
        if (frameMs <= 0)
            return;

        _unsteppedMs += frameMs;

        int steps = (int)(_unsteppedMs / StepDurationMs);
        if (steps <= 0)
            return;

        if (steps > MaxCatchUpSteps)
        {
            steps        = MaxCatchUpSteps;
            _unsteppedMs = 0;
        }
        else
        {
            _unsteppedMs -= steps * StepDurationMs;
        }

        for (int ndx = 0; ndx < steps; ndx++)
        {
            // The run-in plays out first. Nothing is stepped during it, the player's simulation and the
            // opponents' alike, so every runner is still on its step 0 when the run-in reaches the line.
            if (_runInStepsLeft > 0)
            {
                _runInStepsLeft--;
                continue;
            }

            // Past the finish line the player's simulation stands still and nothing is applied or recorded:
            // the step only moves the run-out on, and the opponents with it, so a field still out on the
            // course keeps running to the line, where each leaves in turn.
            if (_simulation.IsOver)
            {
                if (!_simulation.IsFinished)
                    break;

                _runOutSteps++;
                foreach (GhostPlayback ghost in _ghosts)
                    ghost.Advance();
                continue;
            }

            // One transition per step: a tap that went down and back up inside a single frame is still a
            // press and a release the simulation can act on, one step apart.
            if (_pendingTransitions.Count > 0)
            {
                TimeTrialInput input = new TimeTrialInput(_simulation.StepIndex, _pendingTransitions.Dequeue());
                _recording.Inputs.Add(input);
                _simulation.ApplyInput(input);
            }

            foreach (GhostPlayback ghost in _ghosts)
                ghost.Advance();

            int climbsBefore = _simulation.NextClimbIndex;
            int gatesBefore  = _simulation.NextGateIndex;
            _simulation.Step();

            // A segment closes at a climb's bottom torii, where the course takes the runner; the field regroups
            // at the top torii, where it hands them back and the next segment opens.
            for (int climb = climbsBefore; climb < _simulation.NextClimbIndex; climb++)
                PlaceSegment(climb, finishTimeMs: 0);

            for (int gate = gatesBefore; gate < _simulation.NextGateIndex; gate++)
                Regroup(gate);
        }
    }

    /// <summary>
    /// Show every opponent from the moment it crossed the gate the player has just crossed, so the field
    /// regroups and the next segment starts level.
    /// </summary>
    void Regroup(int gateIndex)
    {
        foreach (GhostPlayback ghost in _ghosts)
            ghost.RegroupAt(gateIndex);
    }

    /// <summary>
    /// Rank the segment that just closed against the same segment of every opponent in the lineup, for display.
    /// <paramref name="finishTimeMs"/> is only read for the last segment, which the finish line closes.
    /// </summary>
    void PlaceSegment(int segment, int finishTimeMs)
    {
        if (!_lineupPlaceable)
            return;

        int mine      = TimeTrialSegments.TimeMs(_simulation.GateTimesMs, _simulation.ClimbEntryTimesMs, finishTimeMs, segment);
        int placement = 1;

        // Only a strictly quicker opponent costs a place, so equal times share the better place.
        foreach (TimeTrialGhost opponent in _lineup)
        {
            if (TimeTrialSegments.TimeMs(opponent.GateTimesMs, opponent.ClimbEntryTimesMs, opponent.FinishTimeMs, segment) < mine)
                placement++;
        }

        while (_segmentPlacements.Count < segment)
            _segmentPlacements.Add(0);
        _segmentPlacements.Add(placement);
        SegmentPlaced?.Invoke();
    }

    /// <summary>
    /// Queue a change of the button's state for the next step the run simulates. A transition that does not
    /// change the state is dropped, so the recording alternates press and release as the server requires.
    /// </summary>
    void QueueTransition(bool pressed)
    {
        // The run-in drops a press before the button state is touched, so a press held from the run-in into
        // the run does nothing until it is let go and pressed again.
        if (IsRunningIn || _simulation.IsOver || pressed == _buttonDown)
            return;

        // The cap is checked before the state is updated, so a dropped press cannot leave a lone release behind.
        if (_recording.Inputs.Count + _pendingTransitions.Count >= TimeTrialConstants.MaxInputEvents)
            return;

        _buttonDown = pressed;
        _pendingTransitions.Enqueue(pressed);
    }

    /// <summary>
    /// One opponent being reproduced from its recording on its own simulation. The player's run moves only
    /// where along that recording it is shown from: at each gate the player crosses, the opponent is
    /// re-anchored to its own crossing of the same gate.
    /// </summary>
    sealed class GhostPlayback
    {
        readonly CourseDefinition     _course;
        readonly TimeTrialRules       _rules;
        readonly List<TimeTrialInput> _inputs;

        /// <summary>
        /// This opponent's simulation on the step it crossed each of the course's gates, captured by running
        /// the recording once as far as the last gate. A gate the recording never reaches is null.
        /// </summary>
        readonly TimeTrialSimulation[] _gateSnapshots;

        /// <summary>The next input of the recording to apply from each entry of <see cref="_gateSnapshots"/>.</summary>
        readonly int[] _gateNextInputs;

        int _nextInput;

        public TimeTrialSimulation Simulation { get; private set; }

        /// <summary>The rules this opponent's recording is reproduced by: its own Skill applied.</summary>
        public TimeTrialRules Rules => _rules;

        /// <summary>
        /// Whether a climb has this opponent off screen: from its own arrival at a climb's bottom torii until
        /// <see cref="RegroupAt"/> (<see cref="GhostHideReason.Climb"/>).
        /// </summary>
        public bool Hidden { get; private set; }

        public GhostPlayback(CourseDefinition course, TimeTrialRules rules, List<TimeTrialInput> inputs)
        {
            _course         = course;
            _rules          = rules;
            _inputs         = inputs;
            _gateSnapshots  = new TimeTrialSimulation[course.Gates.Count];
            _gateNextInputs = new int[course.Gates.Count];
            CaptureGateSnapshots();
            Simulation = new TimeTrialSimulation(course, rules);
        }

        public void Advance()
        {
            if (Simulation.IsOver)
                return;

            while (_nextInput < _inputs.Count && _inputs[_nextInput].Step == Simulation.StepIndex)
            {
                Simulation.ApplyInput(_inputs[_nextInput]);
                _nextInput++;
            }

            Simulation.Step();

            // Only the player's regroup brings it back, because the opponent's own crossing of the gate above
            // the climb happens at a different moment from the player's.
            if (Simulation.IsInputLocked)
                Hidden = true;
        }

        /// <summary>
        /// Move this opponent to its own crossing of <paramref name="gateIndex"/> and put it back on screen,
        /// whether that crossing is still ahead of it or already behind it. A gate its recording never reaches
        /// leaves it as it is.
        /// </summary>
        public void RegroupAt(int gateIndex)
        {
            if (gateIndex < 0 || gateIndex >= _gateSnapshots.Length)
                return;

            TimeTrialSimulation snapshot = _gateSnapshots[gateIndex];
            if (snapshot == null)
                return;

            // The captured copy stays untouched, so the opponent always runs on a copy of it.
            Simulation = snapshot.Clone();
            _nextInput = _gateNextInputs[gateIndex];
            Hidden     = false;
        }

        void CaptureGateSnapshots()
        {
            TimeTrialSimulation scratch   = new TimeTrialSimulation(_course, _rules);
            int                 nextInput = 0;
            int                 found     = 0;

            while (found < _gateSnapshots.Length && !scratch.IsOver)
            {
                while (nextInput < _inputs.Count && _inputs[nextInput].Step == scratch.StepIndex)
                    scratch.ApplyInput(_inputs[nextInput++]);

                scratch.Step();

                while (found < scratch.GateTimesMs.Count)
                {
                    _gateSnapshots[found]  = scratch.Clone();
                    _gateNextInputs[found] = nextInput;
                    found++;
                }
            }
        }
    }

    void ReportFinished()
    {
        _finishReported   = true;
        ProvisionalResult = _simulation.Result;

        // The finish line closes the last segment, so it is placed here rather than at a bottom torii.
        if (ProvisionalResult != null)
            PlaceSegment(ProvisionalResult.ClimbEntryTimesMs.Count, ProvisionalResult.FinishTimeMs);

        Phase = TimeTrialPhase.Finished;
        Finished?.Invoke();
    }
}
