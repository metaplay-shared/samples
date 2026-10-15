using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;

namespace Game.Server
{
    /// <summary>
    /// Options controlling how the game server serves the Blazor WASM WebClient as a public,
    /// unauthenticated static web app (via <see cref="WebClientHostingController"/> on the
    /// PublicWebApi host).
    /// </summary>
    [RuntimeOptions("WebClientHosting", isStatic: false, "Options for serving the Blazor WASM WebClient from the game server's public web host.")]
    public class WebClientHostingOptions : RuntimeOptionsBase
    {
        [MetaDescription("Filesystem path (relative to the working directory) to the WebClient build output to serve. Empty disables serving.")]
        public string WebRootPath { get; private set; } = IsLocalEnvironment ? "" : "publicwebapp";
    }
}
