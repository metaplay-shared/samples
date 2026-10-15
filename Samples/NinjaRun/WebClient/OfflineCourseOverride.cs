using Game.Logic;
using WebClientBase.Startup;

namespace WebClient;

/// <summary>
/// The map offline practice was asked to open, read from the page URL's query string.
/// </summary>
/// <remarks>
/// The parse runs before the Metaplay host exists, through <see cref="PageQuery"/>, and <c>WebClient/Program.cs</c>
/// latches what it finds here. The id is honoured only alongside <c>?env=offline</c>: online, the map is the
/// server's to pick from the registry's active set. It names a map, the one kind of id <c>CourseResolver</c>
/// answers for.
/// </remarks>
public static class OfflineCourseOverride
{
    /// <summary>The map offline practice opens, or null when the page asked for none or is not offline.</summary>
    public static CourseId RequestedCourseId { get; set; }

    /// <summary>The requested map id, or null when the page asked for none or is not offline.</summary>
    public static string ParseFromQuery()
    {
        string course = PageQuery.Get("course");
        return PageQuery.Get("env") == "offline" && !string.IsNullOrEmpty(course) ? course : null;
    }
}
