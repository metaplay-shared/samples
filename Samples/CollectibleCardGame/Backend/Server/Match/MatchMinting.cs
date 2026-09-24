using Game.Logic;
using Metaplay.Core;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    /// <summary>
    /// Mint a table: draw a random id, ask it to set up, and retry an id collision. The practice path and the
    /// matchmaker share it.
    /// </summary>
    public static class MatchMinting
    {
        /// <summary> How many random ids a mint tries before giving up. </summary>
        const int Attempts = 4;

        /// <summary>
        /// Returns the minted id, or <see cref="EntityId.None"/> when no table could be committed. A failure
        /// other than a collision does not say whether the table exists, so that table is asked to abandon
        /// itself, which it grants only while nobody has joined and nothing has happened at it.
        /// </summary>
        public static async Task<EntityId> MintAsync(Func<EntityId, Task> setupAsync, Func<EntityId, Task> abandonAsync, IMetaLogger log)
        {
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                // EntityId.CreateRandom is not guaranteed unique; a second setup at a live id is refused.
                EntityId matchId = EntityId.CreateRandom(EntityKindGame.Match);

                try
                {
                    await setupAsync(matchId);
                    return matchId;
                }
                catch (InternalEntitySetupRefusal)
                {
                    // The table that refused is somebody else's, so it is left alone.
                    log.Info("Match id {MatchId} was taken; minting another", matchId);
                }
                catch (Exception ex)
                {
                    log.Warning("Setting up table {MatchId} failed with {Error}; asking it to stand down", matchId, ex.GetType().Name);
                    try
                    {
                        await abandonAsync(matchId);
                        log.Info("Orphaned table {MatchId} stood down", matchId);
                    }
                    catch (Exception abandonError)
                    {
                        log.Info("Orphaned table {MatchId} could not be asked to stand down ({Error}); its join window collects it", matchId, abandonError.GetType().Name);
                    }

                    return EntityId.None;
                }
            }

            return EntityId.None;
        }
    }
}
