// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

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
        public ServerMain()
        {
        }

        protected override void HandleKeyPress(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.K:
                    MetaTime before = MetaTime.Now;
                    MetaTime.DebugTimeOffset += MetaDuration.FromMinutes(10);
                    MetaTime after = MetaTime.Now;
                    Console.WriteLine("Skipping 10min forward from {0} to {1}", before, after);
                    break;

                default:
                    break;
            }
        }

        static async Task<int> Main(string[] cmdLineArgs)
        {
            using ServerMain program = new ServerMain();
            return await program.RunServerAsync(cmdLineArgs);
        }
    }
}
