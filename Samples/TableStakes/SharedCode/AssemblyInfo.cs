using System.Runtime.CompilerServices;

// SharedCode.Tests calls MatchEngine.CreateFromDeal and PlayerModel.TryRecordMatch, and adds test observers through
// PlayerModel._testMatchCompletionObservers. Each member's comment says why it is internal.
[assembly: InternalsVisibleTo("SharedCode.Tests")]

// WebClient.Tests and Server.Tests build PlayerPublicIdentity directly, because testing that equipped cosmetics
// reach every standings row and table seat needs an identity with a frame, and the public ForBot cannot set one.
// A missing cosmetic raises no error, because a CSS class that matches no rule is not an error in a browser.
[assembly: InternalsVisibleTo("WebClient.Tests")]
[assembly: InternalsVisibleTo("Server.Tests")]

