using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Server.PublicWebApi;
using Microsoft.AspNetCore.Mvc;

namespace Game.Server
{
    /// <summary>
    /// Sends a player who opens the root of the game server's public web host on to the web client, at the root of the
    /// environment's public CDN where <c>metaplay deploy webclient</c> uploads it, with a 302 that keeps the query.
    /// </summary>
    public class WebClientRedirectController : PublicWebApiController
    {
        [HttpGet("/")]
        public IActionResult RedirectToWebClient()
        {
            string cdnUrl = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>().S3PublicBucketCdnUrl;
            string target = RedirectTarget(cdnUrl, Request.QueryString.Value);
            return target == null ? NotFound() : Redirect(target);
        }

        /// <summary>
        /// The web client's address with <paramref name="query"/> (with its leading <c>?</c>, or empty), or null when
        /// the environment has no public CDN, as a local server has none.
        /// </summary>
        public static string RedirectTarget(string cdnUrl, string query) =>
            string.IsNullOrEmpty(cdnUrl) ? null : cdnUrl + query;
    }
}
