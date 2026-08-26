// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Server.GuildDiscovery;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Model;
using Metaplay.Server.Guild;
using Metaplay.Server.GuildDiscovery;
using System;

namespace Game.Server.Guild
{
    [EntityConfig]
    public class GuildConfig : GuildEntityConfigBase
    {
        public override Type EntityActorType => typeof(GuildActor);
    }

    /// <summary>
    /// Entity actor class representing a guild.
    /// </summary>
    public sealed class GuildActor : GuildActorBase<GuildModel, PersistedGuild>, IGuildModelServerListener
    {
        protected override sealed TimeSpan TickUpdateInterval => TimeSpan.FromSeconds(10);

        protected override void OnSwitchedToModel(GuildModel model)
        {
            model.ServerListener = this;
        }

        protected override bool ShouldAcceptPlayerJoin(ShouldAcceptPlayerJoinArgs args)
        {
            // If we are too full, don't allow new players
            if (Model.Members.Count >= Model.MaxNumMembers)
                return false;

            // Enforce join requirement. In Idler, we only have the level requirement (bypass for invited players).
            if (args is ShouldAcceptPlayerJoinArgs.InvitationCodeJoinArgs)
            {
                // invited player, accept
                return true;
            }

            // Non-invite path: Check the requirements.
            GuildMemberPlayerData data = (GuildMemberPlayerData)args.PlayerData;
            if (data.PlayerLevel < Model.RequiredPlayerLevel)
                return false;

            return true;
        }

        protected override PersistedGuild CreatePersisted(EntityId entityId, DateTime persistedAt, byte[] payload, int schemaVersion, bool isFinal)
        {
            return new PersistedGuild()
            {
                EntityId            = entityId.ToString(),
                PersistedAt         = persistedAt,
                Payload             = payload,
                SchemaVersion       = schemaVersion,
                IsFinal             = isFinal,
                RequiredPlayerLevel = Model.RequiredPlayerLevel,
            };
        }

        protected override (GuildDiscoveryInfoBase, GuildDiscoveryServerOnlyInfoBase) CreateGuildDiscoveryInfo()
        {
            return
            (
                new GuildDiscoveryInfo(
                    guildId:                _entityId,
                    displayName:            Model.DisplayName,
                    numMembers:             Model.Members.Count,
                    maxNumMembers:          Model.MaxNumMembers,
                    requiredPlayerLevel:    Model.RequiredPlayerLevel
                    ),
                new GuildDiscoveryServerOnlyInfo(
                    guildCreatedAt:         Model.CreatedAt,
                    memberOnlineLatestAt:   Model.GetMemberOnlineLatestAt(timestampNow: MetaTime.Now)
                    )
            );
        }

        protected override sealed void SetupGuildWithCreationParams(GuildCreationParamsBase baseArgs)
        {
            GuildCreationParams args = (GuildCreationParams)baseArgs;

            // Custom data here.
        }

        protected override object GetMemberGdprExportExtraData(EntityId memberPlayerId)
        {
            // we don't have extra data
            return null;
        }
    }
}
