// Compiled into tools/GameConfigGen. The file-based apps in tools/ carry their own copy, since sharing it with
// #:include needs .NET SDK 10.0.300 or later.

using System.IO;

/// <summary>The checkout a tool is run in.</summary>
static class RepoRoot
{
    /// <summary>
    /// The nearest directory at or above the working directory that holds <c>metaplay-project.yaml</c>, or null
    /// when the tool is run outside a checkout.
    /// </summary>
    public static string TryFind()
    {
        DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
            directory = directory.Parent;

        return directory?.FullName;
    }
}
