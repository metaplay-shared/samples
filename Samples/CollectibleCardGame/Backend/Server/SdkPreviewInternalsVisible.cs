using System.Runtime.CompilerServices;

// The vendored SDK copy in SdkPreview/ is internal, as in the SDK. Its per-subscriber substitution keeps one
// seat's cards off the other seat's wire, so VendoredSdkBaseTests tests it. Removed with SdkPreview on R39.
[assembly: InternalsVisibleTo("Server.Tests")]
