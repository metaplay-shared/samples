using Metaplay.Core;
using Metaplay.Server;
using System;
using System.Threading.Tasks;

namespace Game.Server
{
    /// <summary>
    /// Game-specific server entrypoint.
    /// </summary>
    class ServerMain : ServerMainBase
    {
        static async Task<int> Main(string[] cmdLineArgs)
        {
            using ServerMain program = new ServerMain();
            return await program.RunServerAsync(cmdLineArgs);
        }
    }
}
