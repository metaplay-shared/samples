using Metaplay.Cloud.Entity;
using Metaplay.Server;
using System;

namespace Game.Server
{
    [EntityConfig]
    public class SessionConfig : SessionConfigBase
    {
        public override Type EntityActorType => typeof(SessionActor);
    }

    public class SessionActor : SessionActorBase
    {
    }
}
