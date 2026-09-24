using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Server;

/// <summary>Only content-addressed URLs may outlive a release without revalidation.</summary>
public static class WebClientCachePolicy
{
    public const string VersionPlaceholder = "__CLIENT_VERSION__";
    static readonly Regex FingerprintedFrameworkFile = new Regex(
        @"^_framework/[^/]+\.[a-z0-9]{10}\.(wasm|js|dat|dll|blat)(\.(br|gz))?$", RegexOptions.CultureInvariant);

    public static string CacheControl(string relativePath)
        => FingerprintedFrameworkFile.IsMatch(relativePath.Replace('\\', '/'))
            ? "public, max-age=31536000, immutable" : "no-cache";

    /// <summary>
    /// Hash the entry points, including dotnet.js (which carries the assembly manifest), so each release
    /// gets new entry-point URLs.
    /// </summary>
    public static string Version(string root)
    {
        string[] files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is ".html" or ".css" or ".js")
            .Concat(new[] { Path.Combine(root, "_framework", "dotnet.js"),
                Path.Combine(root, "_content", "ClientBase", "css", "app-base.css") })
            .Where(File.Exists).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path)));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
