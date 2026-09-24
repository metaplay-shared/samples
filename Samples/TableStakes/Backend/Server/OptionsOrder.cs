using System;

namespace Game.Server
{
    /// <summary>
    /// Checks the order of two runtime option durations when the options load. A thrown exception stops the server
    /// from starting, and refuses a live reload, so a wrong order is reported instead of reaching players.
    /// </summary>
    public static class OptionsOrder
    {
        /// <summary>
        /// Throws unless <paramref name="shorter"/> is shorter than <paramref name="longer"/>.
        /// </summary>
        /// <param name="section">The options section, for example <c>Matchmaking</c>.</param>
        /// <param name="consequence">What goes wrong for players when the order is broken.</param>
        public static void ThrowIfNotShorter(string section, TimeSpan shorter, string shorterName, TimeSpan longer, string longerName, string consequence)
        {
            if (shorter < longer)
                return;

            throw new InvalidOperationException(
                $"{section} options are out of order: {shorterName} ({shorter}) must be shorter than {longerName} ({longer}). "
                + consequence);
        }
    }
}
