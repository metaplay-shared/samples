using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Server.PublicWebApi;
using Microsoft.AspNetCore.Mvc;

namespace Game.Server
{
    /// <summary>
    /// Sends a player who opens the root of the game server's public web host, the address the web client had before
    /// it moved to the environment's CDN, on to the web client at the root of that CDN, with the request's query.
    /// </summary>
    public class WebClientRedirectController : PublicWebApiController
    {
        [HttpGet("/")]
        public IActionResult RedirectToWebClient()
        {
            BlobStorageOptions blobStorage = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>();
            string             url         = WebClientUrl(blobStorage.S3PublicBucketCdnUrl, Request.QueryString.Value);
            if (url == null)
                return NotFound();

            return Redirect(url);
        }

        /// <summary>
        /// The web client's address: <paramref name="cdnUrl"/> with <paramref name="query"/> appended, or null when the
        /// environment has no public CDN, as a local server does.
        /// </summary>
        public static string WebClientUrl(string cdnUrl, string query)
            => string.IsNullOrEmpty(cdnUrl) ? null : cdnUrl + query;
    }
}
