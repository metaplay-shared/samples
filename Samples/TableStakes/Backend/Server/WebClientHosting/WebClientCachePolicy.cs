using System;
using System.IO;
using System.Linq;

namespace Game.Server
{
    /// <summary>
    /// The <c>Cache-Control</c> header for a file of the published web client, chosen by its path under the web
    /// root.
    /// <para>
    /// Only a file whose name carries the publish's content hash is sent as immutable. Every other file is sent as
    /// <c>no-cache</c>. That includes <c>dotnet.js</c>, which has no hash but holds the boot manifest that names the
    /// hashed assemblies, so a cached copy would keep a browser on its first build. See docs/web-client.md,
    /// "Updating clients after a deploy".
    /// </para>
    /// </summary>
    public static class WebClientCachePolicy
    {
        public const string Immutable  = "public, max-age=31536000, immutable";
        public const string Revalidate = "no-cache";

        /// <summary>The length of the content hash the publish puts in a file name.</summary>
        const int ContentHashLength = 10;

        /// <summary>
        /// The <c>Cache-Control</c> value for <paramref name="relativePath"/>, a path under the web root with either
        /// directory separator.
        /// </summary>
        public static string CacheControlFor(string relativePath)
        {
            string normalized  = relativePath.Replace('\\', '/');
            bool   inFramework = normalized.StartsWith("_framework/", StringComparison.OrdinalIgnoreCase);
            return inFramework && HasContentHash(Path.GetFileName(normalized)) ? Immutable : Revalidate;
        }

        /// <summary>
        /// Whether <paramref name="fileName"/> has the publish's content hash: a dot-separated segment before the
        /// extension, made of <see cref="ContentHashLength"/> lowercase letters and digits
        /// (<c>dotnet.native.rdro6vsp75.wasm</c>). <see cref="CacheControlFor"/> checks only names under
        /// <c>_framework/</c>, because only the publish names files there.
        /// </summary>
        public static bool HasContentHash(string fileName)
        {
            string[] parts = fileName.Split('.');
            if (parts.Length < 3)
                return false;

            string candidate = parts[parts.Length - 2];
            return candidate.Length == ContentHashLength && candidate.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
        }
    }
}
