namespace Game.Logic
{
    /// <summary>
    /// Fixed properties of the time trial simulation that are part of the ruleset rather than designer
    /// tunables. Changing any of these changes what a recording means, so <see cref="RulesetVersion"/> must be
    /// bumped alongside them. Designer-tunable physics values live in <see cref="TimeTrialRules"/> (game config).
    /// </summary>
    public static class TimeTrialConstants
    {
        /// <summary>Fixed simulation rate. Every input is recorded against a step index at this rate.</summary>
        public const int StepsPerSecond = 60;

        /// <summary>
        /// Identifies the simulation rules a recording was made against. A recording is only meaningful
        /// together with its course and this version; the server refuses recordings from other versions.
        /// Bump it whenever a change to the simulation or to how a course is built makes the same inputs on the
        /// same course produce a different run.
        /// </summary>
        public const int RulesetVersion = 16;

        /// <summary>
        /// Upper bound on the number of steps a run may take, on both the client (the run is force-ended) and
        /// the server (a recording that has not reached the finish by here yields no result). Bounds replay
        /// cost, since the payload is untrusted. Step indices are zero-based, so the last step simulated is
        /// <c>MaxRunSteps - 1</c>.
        /// </summary>
        public const int MaxRunSteps = StepsPerSecond * 180;

        /// <summary>Upper bound on input transitions in one recording, for the same reason as <see cref="MaxRunSteps"/>.</summary>
        public const int MaxInputEvents = 4000;

        /// <summary>Wall-clock milliseconds represented by <paramref name="steps"/> whole simulation steps.</summary>
        public static int StepsToMilliseconds(int steps) => (int)((long)steps * 1000 / StepsPerSecond);
    }
}
