using Metaplay.Core.Client;
using Metaplay.Core.Message;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using System;
using Metaplay.Core.Network;

/// <summary>
/// Example connection handling delegate.
///
/// Inherits most of its behavior from <see cref="DefaultMetaplayConnectionDelegate"/>.
/// If no overrides were needed, <see cref="DefaultMetaplayConnectionDelegate"/>
/// could be used directly, without needing a custom subclass.
/// </summary>
class GameConnectionDelegate : DefaultMetaplayConnectionDelegate
{
    public bool FailNextSessionStart { get; set; } = false;

    public override void OnSessionStarted(SessionProtocol.SessionStartSuccess sessionStart, ClientSessionStartResources startResources)
    {
        if (FailNextSessionStart)
        {
            FailNextSessionStart = false;
            throw new Exception("Simulated session start failure");

        }
        base.OnSessionStarted(sessionStart, startResources);
    }
}
