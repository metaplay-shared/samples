using Game.Logic;
using Metaplay.Core.Math;
using System.Globalization;

namespace WebClient.Gameplay;

/// <summary>
/// How the developer course tools and the part editor write lengths, times and runners. The run screen writes
/// its times through <see cref="FormatMs"/> too.
/// </summary>
public static class CourseToolsText
{
    public static string Metres(F32 value) => Metres(value.Float);

    public static string Metres(float value) => $"{value.ToString("0.#", CultureInfo.InvariantCulture)} m";

    public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A time as ss.mmm, or m:ss.mmm past a minute, dropping the minutes on the sub-minute runs that are the norm.</summary>
    public static string FormatMs(int milliseconds)
    {
        int minutes = milliseconds / 60000;
        int seconds = milliseconds / 1000 % 60;
        int millis  = milliseconds % 1000;

        return minutes > 0
            ? $"{minutes}:{seconds:00}.{millis:000}"
            : $"{seconds:00}.{millis:000}";
    }

    public static string RunnerKey(TimeTrialBotSkill? skill) => skill switch
    {
        null                        => "no-input",
        TimeTrialBotSkill.Clumsy    => "clumsy",
        TimeTrialBotSkill.Cautious  => "cautious",
        TimeTrialBotSkill.Competent => "competent",
        _                           => "sharp",
    };

    public static string RunnerLabel(TimeTrialBotSkill? skill)
        => skill is TimeTrialBotSkill level ? $"{level} ({TimeTrialSeedRunners.NameFor(level)})" : "No input";
}
