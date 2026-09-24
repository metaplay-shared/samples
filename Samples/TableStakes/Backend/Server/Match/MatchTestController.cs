using Game.Logic;
using Metaplay.Core;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    /// <summary>
    /// Test-only endpoint that makes a table's pending timers fire now (<see cref="MatchForceExpireRequest"/>).
    /// It returns how many deadlines were moved, so a test can poll the result instead of sleeping
    /// (<c>docs/testing.md</c>, "Forcing timers").
    /// <para>
    /// The route is unauthenticated, so it is enabled only by <see cref="TestRoutesOptions"/>, which only the
    /// E2E harness turns on. Any other server answers 404.
    /// </para>
    /// </summary>
    public class MatchTestController : TestRouteController
    {
        /// <summary>The response: how many deadlines were moved, and the table's phase and play index after.</summary>
        public class ForceExpireResult
        {
            public string MatchId   { get; set; }
            public int    Expired   { get; set; }
            public string Phase     { get; set; }
            public int    PlayIndex { get; set; }
        }

        [HttpPost("test/match/{matchIdStr}/expire")]
        public async Task<IActionResult> ForceExpire(string matchIdStr)
        {
            EntityId matchId;
            try
            {
                matchId = EntityId.ParseFromString(matchIdStr);
            }
            catch (Exception)
            {
                return BadRequest(new { error = $"'{matchIdStr}' is not an entity id" });
            }

            if (matchId.Kind != EntityKindGame.Match)
                return BadRequest(new { error = $"{matchId} is not a table" });

            MatchForceExpireResponse response;
            try
            {
                response = await EntityAskAsync(matchId, MatchForceExpireRequest.Instance);
            }
            catch (Exception ex)
            {
                // The table does not exist, cannot be read, or did not answer. Return the exception message
                // instead of a 500 with a stack trace.
                return NotFound(new { error = $"{matchId} did not answer: {ex.Message}" });
            }

            return Ok(new ForceExpireResult
            {
                MatchId   = matchId.ToString(),
                Expired   = response.NumDeadlinesMoved,
                Phase     = response.Phase.ToString(),
                PlayIndex = response.PlayIndex,
            });
        }
    }
}
