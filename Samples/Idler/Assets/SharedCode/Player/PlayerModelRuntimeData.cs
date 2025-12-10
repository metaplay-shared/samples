// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Player;

namespace Game.Logic
{
    public sealed class PlayerModelRuntimeData : PlayerModelRuntimeDataBase<PlayerModel>
    {
        readonly IPlayerModelServerListener _serverListener;
        readonly IPlayerModelClientListener _clientListener;

        public PlayerModelRuntimeData(PlayerModel instance)
            : base(instance)
        {
            _serverListener = instance.ServerListener;
            _clientListener = instance.ClientListener;
        }

        public override void CopySideEffectListenersTo(PlayerModel instance)
        {
            base.CopySideEffectListenersTo(instance);

            instance.ServerListener = _serverListener;
            instance.ClientListener = _clientListener;
        }
    }
}
