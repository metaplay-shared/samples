using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Server.PublicWebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.IO;

namespace Game.Server
{
    /// <summary>
    /// Serves the Blazor WASM WebClient from <see cref="WebClientHostingOptions.WebRootPath"/> as a public,
    /// unauthenticated static web app. Deriving from <see cref="PublicWebApiController"/> registers it on the
    /// public PublicWebApi host, not on the authenticated AdminApi host that serves the LiveOps Dashboard.
    /// <para>
    /// The <c>GET</c> route is a catch-all, so it has the lowest route precedence and never hides more specific
    /// PublicWebApi routes such as <c>/auth/...</c>. A pre-compressed <c>.br</c> or <c>.gz</c> file from the
    /// publish is served when the client accepts that encoding.
    /// </para>
    /// </summary>
    public class WebClientHostingController : PublicWebApiController
    {
        // Serve .wasm as application/wasm. Blazor's .dat, .dll and .blat files fall back to application/octet-stream.
        static readonly FileExtensionContentTypeProvider s_contentTypeProvider = CreateContentTypeProvider();

        static FileExtensionContentTypeProvider CreateContentTypeProvider()
        {
            FileExtensionContentTypeProvider provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".wasm"] = "application/wasm";
            return provider;
        }

        [HttpGet("{**path}")]
        public IActionResult ServeWebClient(string path)
        {
            WebClientHostingOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<WebClientHostingOptions>();
            if (string.IsNullOrEmpty(options.WebRootPath))
                return NotFound();

            // A missing root needs no check of its own: no file under it exists, so every path below ends in 404.
            string rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), options.WebRootPath));

            // Map the request to a file under the web root, guarding against path traversal.
            string requestedRelative = string.IsNullOrEmpty(path) ? "index.html" : path;
            string fullPath = Path.GetFullPath(Path.Combine(rootPath, requestedRelative));

            bool isUnderRoot = fullPath == rootPath || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (isUnderRoot && System.IO.File.Exists(fullPath))
                return ServeFile(rootPath, fullPath);

            // No file matched. Fall back to index.html only for client-side navigation routes, which have no
            // file extension. A path with an extension is a missing file: return 404, because serving HTML
            // in place of a .js or .wasm file causes a MIME type or integrity error in the browser.
            if (Path.HasExtension(requestedRelative))
                return NotFound();

            // Serve index.html so the client-side router can handle the path.
            string indexPath = Path.Combine(rootPath, "index.html");
            if (System.IO.File.Exists(indexPath))
                return ServeFile(rootPath, indexPath);

            return NotFound();
        }

        IActionResult ServeFile(string rootPath, string fullPath)
        {
            if (!s_contentTypeProvider.TryGetContentType(fullPath, out string contentType))
                contentType = "application/octet-stream";

            // Choose Cache-Control from the requested file's path, not from the compressed file served below.
            string relative = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
            Response.Headers.CacheControl = WebClientCachePolicy.CacheControlFor(relative);

            // Serve the pre-compressed .br or .gz file if the client accepts that encoding. Content-Type stays
            // that of the original file and Content-Encoding names the compression. Vary makes shared caches
            // store each encoding separately.
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
