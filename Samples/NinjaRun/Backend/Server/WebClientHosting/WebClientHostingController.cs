using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Server.PublicWebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Game.Server
{
    /// <summary>
    /// Serves the Blazor WASM WebClient as a public, unauthenticated static web app, from the directory
    /// configured by <see cref="WebClientHostingOptions.WebRootPath"/>.
    /// </summary>
    public class WebClientHostingController : PublicWebApiController
    {
        // .wasm must be served as application/wasm. An extension with no mapping is served as application/octet-stream.
        static readonly FileExtensionContentTypeProvider s_contentTypeProvider = CreateContentTypeProvider();

        /// <summary>
        /// A file named after its own content, as the served art and audio are:
        /// <c>&lt;name&gt;.sha256-&lt;hash&gt;.&lt;ext&gt;</c>.
        /// </summary>
        static readonly Regex s_contentHashedName = new Regex(@"\.sha256-[0-9a-f]{8,64}\.[A-Za-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// A <c>_framework</c> file the WebAssembly publish fingerprinted: a ten-character lowercase
        /// base-36 hash inserted before the extension, as in <c>dotnet.runtime.v06hirbjsv.js</c> or
        /// <c>GameLogic.6ai1c59iff.wasm</c>. The publish leaves <c>dotnet.js</c> and
        /// <c>blazor.webassembly.js</c> unhashed.
        /// </summary>
        static readonly Regex s_fingerprintedFrameworkName = new Regex(@"\.[0-9a-z]{10}\.[A-Za-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static FileExtensionContentTypeProvider CreateContentTypeProvider()
        {
            FileExtensionContentTypeProvider provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".wasm"] = "application/wasm";
            return provider;
        }

        // The catch-all has the lowest route precedence, so more specific PublicWebApi routes are never shadowed.
        [HttpGet("{**path}")]
        public IActionResult ServeWebClient(string path)
        {
            WebClientHostingOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<WebClientHostingOptions>();
            if (string.IsNullOrEmpty(options.WebRootPath))
                return NotFound();

            string rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), options.WebRootPath));
            if (!Directory.Exists(rootPath))
                return NotFound();

            // Map the request to a file under the web root, guarding against path traversal.
            string requestedRelative = string.IsNullOrEmpty(path) ? "index.html" : path;
            string fullPath = Path.GetFullPath(Path.Combine(rootPath, requestedRelative));

            bool isUnderRoot = fullPath == rootPath || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (isUnderRoot && System.IO.File.Exists(fullPath))
                return ServeFile(rootPath, fullPath);

            // A missing path with a file extension is a missing asset. HTML served in its place surfaces in
            // the browser as a cryptic MIME or SRI integrity failure, so it gets a 404.
            if (Path.HasExtension(requestedRelative))
                return NotFound();

            // An extensionless path is a client-side (SPA) route, served index.html.
            string indexPath = Path.Combine(rootPath, "index.html");
            if (System.IO.File.Exists(indexPath))
                return ServeFile(rootPath, indexPath);

            return NotFound();
        }

        IActionResult ServeFile(string rootPath, string fullPath)
        {
            if (!s_contentTypeProvider.TryGetContentType(fullPath, out string contentType))
                contentType = "application/octet-stream";

            string fileName = Path.GetFileName(fullPath);

            // A file whose name carries its content hash is cached immutably, since a change to it is a new
            // name. Every other file keeps its name across a redeploy (index.html, the unhashed _framework
            // loaders such as dotnet.js with the boot config inside, and the ES modules and stylesheets that
            // import one another by plain name), so it always revalidates, or a redeploy pairs stale files
            // with fresh ones. PhysicalFile answers a matching If-Modified-Since with 304.
            string relative = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
            bool isFramework = relative.StartsWith("_framework" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            bool isFingerprintedAsset = (isFramework && s_fingerprintedFrameworkName.IsMatch(fileName))
                || s_contentHashedName.IsMatch(fileName);

            Response.Headers.CacheControl = isFingerprintedAsset ? "public, max-age=31536000, immutable" : "no-cache";

            // A pre-compressed sibling (.br/.gz) the publish produced is served when the client accepts it.
            // The Content-Type stays that of the original file, and Content-Encoding marks the wire format.
            // Vary lets shared caches key on the negotiated encoding.
            Response.Headers.Vary = "Accept-Encoding";
            string acceptEncoding = Request.Headers.AcceptEncoding.ToString();
            if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(fullPath + ".br"))
            {
                Response.Headers.ContentEncoding = "br";
                return PhysicalFile(fullPath + ".br", contentType);
            }
            if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(fullPath + ".gz"))
            {
                Response.Headers.ContentEncoding = "gzip";
                return PhysicalFile(fullPath + ".gz", contentType);
            }

            return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
        }
    }
}
