using Game.Logic;
using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.MultiplayerEntity;
using UnityEngine;

public class PartyClient : MultiplayerEntityClientBase<PartyModel>
{
    public override ClientSlot ClientSlot => ClientSlotGame.Party;
}
