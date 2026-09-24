using Metaplay.Core;
using Metaplay.Core.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Game.WebAssemblySerializerGen
{
    /// <summary>
    /// Emits <c>Metaplay.Generated.Browser.dll</c>, the pre-built serializer the WebAssembly Client loads at
    /// runtime (mono-wasm cannot generate it on the fly). Mirrors what <c>Unity/Editor/SerializerBuilder.cs</c> does
    /// for Unity player builds, but as a standalone console tool.
    ///
    /// Invoked by the Client build itself (the GenerateBrowserSerializer target in Client.csproj), which passes
    /// the output directory under Client/obj/ - there is no manual regen chore and nothing checked in.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string outputDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outputDir);

            // The assembly name the browser build of the SDK loads at startup: the SDK's engine integration
            // reports "Metaplay.Generated.Browser" as its GeneratedSerializerAssemblyName. The SDK helper that
            // composes the name is internal, so the name is spelled out here.
            const string dllFileName = "Metaplay.Generated.Browser.dll";

            // Initialize the SDK with runtime serializer generation enabled. This scans the entry assembly's
            // integration reference graph (which includes GameLogic, the shared game types) and populates the
            // serializer type registry.
            MetaplayCore.ClientIntegrationAssemblies = IntegrationAssembly.FindRoots().ToList();
            MetaplayCore.InitializeForExternalApp("WebAssemblyGen");

            // Emit the serializer the Client loads at startup, by the name the browser build's engine
            // integration reports as its GeneratedSerializerAssemblyName.
            // enableCaching:true: the .hash sidecar makes a regen with unchanged types a no-op. The compile itself
            // is not byte-deterministic, so without the cache every regen rewrites the checked-in .dll/.pdb with a
            // meaningless binary diff — and regen runs on every Rider client launch (see .run/Client.run.xml).
            // generateRuntimeTypeInfo:true embeds the type metadata so no runtime scanning is needed.
            // isMono / useMemberAccessTrampolines:true: the wasm interpreter enforces member accessibility, so the
            // default direct private-field access throws FieldAccessException at deserialization time (e.g. on
            // MetaActivableSet._activableStates). The trampoline path routes member access through generated accessor
            // methods instead, which the interpreter accepts. (This mirrors the Mono/IL2CPP serializer build.)
            MetaSerializerTypeInfo typeInfo = MetaplayServices.Get<MetaSerializerTypeRegistry>().TypeInfo;
            RoslynSerializerCompileCache.EnsureDllUpToDate(
                outputDir,
                dllFileName,
                Path.Combine(outputDir, "Errors"),
                true,  // enableCaching
                true,  // forceRoslyn
                true,  // isMono
                true,  // useMemberAccessTrampolines
                true,  // generateRuntimeTypeInfo
                typeInfo);

            Console.WriteLine($"{Path.Combine(outputDir, dllFileName)} is up to date (recompiled only if the generated source changed)");
            return 0;
        }
    }
}
