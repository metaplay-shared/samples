using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;

namespace Game.Server.Community;

/// <summary>
/// How often the community snapshot may be asked for and re-sampled (<c>Docs/community.md</c>). Both bound the
/// load the clients' polling puts on the singleton; the client sets its own polling period.
/// </summary>
[RuntimeOptions("Community", isStatic: false, "Request and sampling limits for the leaderboard and activity snapshot.")]
public class CommunityOptions : RuntimeOptionsBase
{
    [MetaDescription("The shortest gap between two snapshot requests a player actor answers. A request inside it is dropped, and the client asks again on its next poll.")]
    public TimeSpan RequestInterval { get; private set; } = TimeSpan.FromSeconds(5);

    [MetaDescription("How long the community actor reuses its online and queue counts before it asks the stats collector and the matchmaker again. Match and leaderboard figures are read fresh on every request.")]
    public TimeSpan SampleInterval { get; private set; } = TimeSpan.FromSeconds(5);
}
