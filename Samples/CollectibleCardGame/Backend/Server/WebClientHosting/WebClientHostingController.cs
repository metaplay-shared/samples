using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Server.PublicWebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.IO;
using System.Collections.Concurrent;

namespace Game.Server
{
    /// <summary>
    /// Serves the Blazor WASM WebClient as a public, unauthenticated static web app.
    ///
    /// <para>
    /// Derives from <see cref="PublicWebApiController"/> so it is auto-registered on the public
    /// (unauthenticated) PublicWebApi host — the right place for a player-facing client, as opposed
    /// to the auth-gated AdminApi host that serves the LiveOps Dashboard. The files are served from
    /// the directory configured by <see cref="WebClientHostingOptions.WebRootPath"/> (the local
    /// build output during development, the baked-in <c>publicwebapp</c> directory in cloud).
    /// </para>
    ///
    /// <para>
    /// The <c>GET</c>-only catch-all route serves any static asset and falls back to
    /// <c>index.html</c> for client-side (SPA) navigation routes only — a missing path that looks
    /// like an asset (has a file extension) returns <c>404</c> rather than HTML, so the browser sees
    /// a clean error instead of a cryptic MIME/integrity failure. It has the lowest route precedence
    /// (catch-all), so more specific PublicWebApi routes (e.g. <c>/auth/...</c> or webhook POSTs) are
    /// never shadowed.
    /// </para>
    ///
    /// <para>
    /// Responses are content-negotiated: a pre-compressed Brotli (<c>.br</c>) or gzip (<c>.gz</c>)
    /// sibling produced by the publish is served when the client accepts it. Fingerprinted
    /// <c>_framework</c> assets are cached immutably for a year; <c>index.html</c> and
    /// all stable URLs revalidate. The index versions its entry-point URLs to bypass older cached loaders.
    /// </para>
    /// </summary>
    public class WebClientHostingController : PublicWebApiController
    {
        // .wasm must be served as application/wasm; Blazor's .dat/.dll/.blat fall back to octet-stream.
        static readonly FileExtensionContentTypeProvider s_contentTypeProvider = CreateContentTypeProvider();
        // Published web roots are immutable for the lifetime of a server process.
        static readonly ConcurrentDictionary<string, string> s_versions = new();

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

            string rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), options.WebRootPath));
            if (!Directory.Exists(rootPath))
                return NotFound();

            // Map the request to a file under the web root, guarding against path traversal.
            string requestedRelative = string.IsNullOrEmpty(path) ? "index.html" : path;
            string fullPath = Path.GetFullPath(Path.Combine(rootPath, requestedRelative));

            bool isUnderRoot = fullPath == rootPath || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (isUnderRoot && System.IO.File.Exists(fullPath))
                return ServeFile(rootPath, fullPath);

            // The request didn't resolve to a real file. Only fall back to index.html for client-side
            // (SPA) navigation routes — i.e. extensionless paths. A request that looks like a static
            // asset (has a file extension) is a genuine miss: return 404 so the browser sees a clean
            // error instead of HTML masquerading as the requested .js/.wasm (which surfaces as a
            // cryptic MIME or SRI integrity failure).
            if (Path.HasExtension(requestedRelative))
                return NotFound();

            // SPA fallback: serve index.html so client-side routes resolve.
            string indexPath = Path.Combine(rootPath, "index.html");
            if (System.IO.File.Exists(indexPath))
                return ServeFile(rootPath, indexPath);

            return NotFound();
        }

        IActionResult ServeFile(string rootPath, string fullPath)
        {
            if (!s_contentTypeProvider.TryGetContentType(fullPath, out string contentType))
                contentType = "application/octet-stream";

            string relative = Path.GetRelativePath(rootPath, fullPath);
            Response.Headers.CacheControl = WebClientCachePolicy.CacheControl(relative);

            if (string.Equals(relative, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                string version = s_versions.GetOrAdd(rootPath, WebClientCachePolicy.Version);
                string html = System.IO.File.ReadAllText(fullPath)
                    .Replace(WebClientCachePolicy.VersionPlaceholder, version, StringComparison.Ordinal);
                return Content(html, "text/html; charset=utf-8");
            }

            // Content negotiation: serve a pre-compressed sibling (.br/.gz) the publish produced when
            // the client accepts it. The Content-Type stays that of the original file; Content-Encoding
            // marks the wire format. Vary lets shared caches key on the negotiated encoding.
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
