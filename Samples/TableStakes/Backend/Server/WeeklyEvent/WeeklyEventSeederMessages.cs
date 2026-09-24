using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Server.WeeklyEvent
{
    /// <summary>
    /// Asks the seeder for the result of its last pass, and optionally runs a pass first.
    /// <para>
    /// With <see cref="RunNow"/> <c>true</c>, the seeder runs the same pass it runs on its own schedule, so tests
    /// check idempotency against the real code. With <see cref="RunNow"/> <c>false</c>, it only reports the last
    /// pass, so a test can wait for the server's <i>own</i> first pass and read its result. Otherwise a test
    /// that started early would run the first pass itself and could not tell it apart from the scheduled pass.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.WeeklyEventSeedPassRequest, MessageDirection.ServerInternal)]
    public class WeeklyEventSeedPassRequest : EntityAskRequest<WeeklyEventSeedPassResponse>
    {
        /// <summary>Whether to run a pass, or only report the last one.</summary>
        public bool RunNow { get; private set; }

        WeeklyEventSeedPassRequest() { }

        public WeeklyEventSeedPassRequest(bool runNow)
        {
            RunNow = runNow;
        }

        public static readonly WeeklyEventSeedPassRequest RunOne = new WeeklyEventSeedPassRequest(runNow: true);
        public static readonly WeeklyEventSeedPassRequest ReportOnly = new WeeklyEventSeedPassRequest(runNow: false);

        public override string ToString() => RunNow ? "run one weekly-event seeding pass" : "report the last weekly-event seeding pass";
    }

    /// <summary>
    /// The result of a seeding pass. <see cref="NumWeeksCreated"/> and <see cref="NumWeeksKept"/> show idempotency: a
    /// pass over a timeline that already covers the horizon creates nothing and keeps every week.
    /// </summary>
    [MetaMessage(MessageCodes.WeeklyEventSeedPassResponse, MessageDirection.ServerInternal)]
    public class WeeklyEventSeedPassResponse : EntityAskResponse
    {
        /// <summary>
        /// Whether the pass completed. If false, nothing was written and <see cref="Problems"/> says why.
        /// </summary>
        public bool Ran { get; private set; }

        /// <summary>The number of weeks this pass added to the timeline.</summary>
        public int NumWeeksCreated { get; private set; }

        /// <summary>The number of weeks already on the timeline, which the pass left unchanged.</summary>
        public int NumWeeksKept { get; private set; }

        /// <summary>The time the last seeded week stops scoring, after this pass.</summary>
        public MetaTime SeededThrough { get; private set; }

        /// <summary>Why the pass could not run, or the errors the timeline reported.</summary>
        public List<string> Problems { get; private set; }

        /// <summary>
        /// The number of passes this seeder has completed, including this one. Zero means the server has not run
        /// its first pass yet, so a test waits until it is nonzero.
        /// </summary>
        public int PassesRun { get; private set; }

        WeeklyEventSeedPassResponse() { }

        public WeeklyEventSeedPassResponse(bool ran, int created, int kept, MetaTime seededThrough, List<string> problems, int passesRun = 0)
        {
            Ran             = ran;
            NumWeeksCreated = created;
            NumWeeksKept    = kept;
            SeededThrough   = seededThrough;
            Problems        = problems ?? new List<string>();
            PassesRun       = passesRun;
        }

        /// <summary>Returns a copy of this response with <see cref="PassesRun"/> set.</summary>
        public WeeklyEventSeedPassResponse WithPassesRun(int passesRun) =>
            new WeeklyEventSeedPassResponse(Ran, NumWeeksCreated, NumWeeksKept, SeededThrough, Problems, passesRun);

        public static WeeklyEventSeedPassResponse Failed(params string[] problems) =>
            new WeeklyEventSeedPassResponse(ran: false, created: 0, kept: 0, seededThrough: MetaTime.Epoch, problems: new List<string>(problems));

        public override string ToString() => Ran
            ? $"created {NumWeeksCreated}, kept {NumWeeksKept}, seeded through {SeededThrough} (pass {PassesRun})"
            : $"did not run: {string.Join("; ", Problems)}";
    }
}
