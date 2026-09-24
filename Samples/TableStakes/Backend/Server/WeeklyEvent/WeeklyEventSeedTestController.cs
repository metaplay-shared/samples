using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.WeeklyEvent
{
    /// <summary>
    /// Test-only endpoints to run a seeding pass on demand and read its result (<c>docs/weekly-event.md</c>).
    /// <para>
    /// <b>They create no events themselves.</b> They ask <see cref="WeeklyEventSeederActor"/> to run its normal
    /// pass, so a test can check that a second real pass changes nothing. They answer 404 unless
    /// <see cref="TestRoutesOptions"/> enables test routes, which only the E2E harness does.
    /// </para>
    /// </summary>
    public class WeeklyEventSeedTestController : TestRouteController
    {
        /// <summary>The JSON report of one pass.</summary>
        public class SeedPassReport
        {
            /// <summary>Whether the pass completed. If false, nothing was written.</summary>
            public bool Ran { get; set; }

            /// <summary>
            /// The number of weeks this pass added to the timeline. Zero when the horizon was already covered.
            /// </summary>
            public int Created { get; set; }

            /// <summary>The number of weeks already on the timeline, which the pass left unchanged.</summary>
            public int Kept { get; set; }

            /// <summary>The time the last seeded week stops scoring, in ISO 8601 format.</summary>
            public string SeededThrough { get; set; }

            /// <summary>Why the pass could not run.</summary>
            public List<string> Problems { get; set; }

            /// <summary>
            /// The number of passes the seeder has completed. Zero means the server has not run its first pass
            /// yet, so a test waits until it is nonzero.
            /// </summary>
            public int PassesRun { get; set; }
        }

        /// <summary>
        /// Returns the last pass's report without running a pass. A test uses this to wait for the server's
        /// <b>own</b> first pass, because running a pass itself would make the two indistinguishable.
        /// </summary>
        [HttpGet("test/weeklyevent/seed")]
        public Task<IActionResult> ReportLastSeedPass() => AskSeederAsync(WeeklyEventSeedPassRequest.ReportOnly);

        /// <summary>Runs the seeder's normal pass now and returns its report.</summary>
        [HttpPost("test/weeklyevent/seed")]
        public Task<IActionResult> RunSeedPass() => AskSeederAsync(WeeklyEventSeedPassRequest.RunOne);

        async Task<IActionResult> AskSeederAsync(WeeklyEventSeedPassRequest request)
        {
            WeeklyEventSeedPassResponse pass;
            try
            {
                pass = await EntityAskAsync(WeeklyEventSeederActor.SingletonId, request);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"the weekly-event seeder did not answer: {ex.Message}" });
            }

            return Ok(new SeedPassReport
            {
                Ran           = pass.Ran,
                Created       = pass.NumWeeksCreated,
                Kept          = pass.NumWeeksKept,
                SeededThrough = pass.SeededThrough.ToDateTime().ToString("O"),
                Problems      = pass.Problems,
                PassesRun     = pass.PassesRun,
            });
        }
    }
}
