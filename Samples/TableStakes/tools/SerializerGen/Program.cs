using Metaplay.Core;
using Metaplay.Core.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Game.WebAssemblySerializerGen
{
    /// <summary>
    /// Writes <c>Metaplay.Generated.Browser.dll</c>, the pre-built serializer that the WebAssembly WebClient loads
    /// at runtime, because mono-wasm cannot generate a serializer at runtime. Does for the WebClient what
    /// <c>Unity/Editor/SerializerBuilder.cs</c> does for Unity player builds.
    ///
    /// The WebClient build runs this automatically. To run it by hand:
    ///   dotnet run --project tools/SerializerGen -- WebClient/Serializer
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string outputDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outputDir);

            // Must match the GeneratedSerializerAssemblyName that the browser build of the SDK loads at startup.
            // The SDK helper that composes the name is internal, so the name is written out here.
            const string dllFileName = "Metaplay.Generated.Browser.dll";

            // Initialize the SDK. This scans the assemblies referenced from the entry assembly, including
            // SharedCode.Client with the shared game types, and fills the serializer type registry.
            MetaplayCore.ClientIntegrationAssemblies = IntegrationAssembly.FindRoots().ToList();
            MetaplayCore.InitializeForExternalApp("WebAssemblyGen");

            // enableCaching: skips the Roslyn compile when the generated source has the same hash as the .hash file
            // next to an existing DLL, so the build can run this tool cheaply when the serializable types have
            // not changed.
            // generateRuntimeTypeInfo: embeds the type metadata in the DLL, so the client does not scan types at
            // runtime.
            // isMono and useMemberAccessTrampolines: the wasm interpreter enforces member accessibility, so direct
            // access to private fields throws FieldAccessException during deserialization (for example on
            // MetaActivableSet._activableStates). Trampolines access members through generated accessor methods,
            // which the interpreter allows. The Unity Mono and IL2CPP serializer builds use the same settings.
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

            Console.WriteLine($"Serializer up to date: {Path.Combine(outputDir, dllFileName)}");
            return 0;
        }
    }
}
