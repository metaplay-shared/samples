using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Server.MapRegistry
{
    /// <summary>
    /// Ask the registry which maps a run may be started on right now. The caller picks from the answer rather
    /// than asking the registry to pick, so the pick stays with whoever knows what the player last ran.
    /// </summary>
    [MetaMessage(ServerMessageCodes.MapRegistryActiveSetRequest, MessageDirection.ServerInternal)]
    public class MapRegistryActiveSetRequest : EntityAskRequest<MapRegistryActiveSetResponse>
    {
        public MapRegistryActiveSetRequest() { }
    }

    /// <summary>
    /// The maps a run may be started on. Empty when the registry has published nothing it can serve, which is
    /// the caller's cue to fall back rather than to open a run on a map that does not compose.
    /// </summary>
    [MetaMessage(ServerMessageCodes.MapRegistryActiveSetResponse, MessageDirection.ServerInternal)]
    public class MapRegistryActiveSetResponse : EntityAskResponse
    {
        public List<CourseId> ActiveMapIds { get; private set; }

        public MapRegistryActiveSetResponse() { }
        public MapRegistryActiveSetResponse(List<CourseId> activeMapIds) { ActiveMapIds = activeMapIds; }
    }
}
