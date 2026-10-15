using Metaplay.Core;
using Metaplay.Core.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Game.WebAssemblySerializerGen
{
    /// <summary>
    /// Emits <c>Metaplay.Generated.Browser.dll</c>, the pre-built serializer the WebAssembly WebClient loads at
    /// runtime (mono-wasm cannot generate it on the fly). Mirrors what <c>Unity/Editor/SerializerBuilder.cs</c> does
    /// for Unity player builds, but as a standalone console tool.
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

            // The assembly name the browser build of the SDK loads at startup: the SDK's engine integration
            // reports "Metaplay.Generated.Browser" as its GeneratedSerializerAssemblyName. The SDK helper that
            // composes the name is internal, so the name is spelled out here.
            const string dllFileName = "Metaplay.Generated.Browser.dll";

            // Initialize the SDK with runtime serializer generation enabled. This scans the entry assembly's
            // integration reference graph (which includes GameLogic, the shared game types) and populates the
            // serializer type registry.
            MetaplayCore.ClientIntegrationAssemblies = IntegrationAssembly.FindRoots().ToList();
            MetaplayCore.InitializeForExternalApp("WebAssemblyGen");

            // Emit the serializer the WebClient loads at startup, by the name the browser build's engine
            // integration reports as its GeneratedSerializerAssemblyName.
            // generateRuntimeTypeInfo:true embeds the type metadata so no runtime scanning is needed.
            // isMono / useMemberAccessTrampolines:true: the wasm interpreter enforces member accessibility, so the
            // default direct private-field access throws FieldAccessException at deserialization time (e.g. on
            // MetaActivableSet._activableStates). The trampoline path routes member access through generated accessor
            // methods instead, which the interpreter accepts. (This mirrors the Mono/IL2CPP serializer build.)
            MetaSerializerTypeInfo scannedTypeInfo = MetaplayServices.Get<MetaSerializerTypeRegistry>().TypeInfo;

            // The scanner lists types in the order it discovers them, which follows the order the build fed the
            // SDK's source files to the compiler and so differs between Windows and Linux. The generator assigns
            // dispatch indices in that order, so the same commit would emit a different serializer (and .hash) on
            // each OS. Sorting by name makes the output the same everywhere; the DLL embeds its own type table and
            // the runtime derives dispatch indices from it, so any order is valid as long as it is stable.
            MetaDictionary<Type, MetaSerializableType> sortedSpecs = new MetaDictionary<Type, MetaSerializableType>(capacity: scannedTypeInfo.Specs.Count);
            foreach ((Type type, MetaSerializableType spec) in scannedTypeInfo.Specs.OrderBy(entry => entry.Key.ToNamespaceQualifiedTypeString(), StringComparer.Ordinal))
                sortedSpecs.Add(type, spec);
            MetaSerializerTypeInfo typeInfo = new MetaSerializerTypeInfo { Specs = sortedSpecs, FullTypeHash = scannedTypeInfo.FullTypeHash };

            RoslynSerializerCompileCache.EnsureDllUpToDate(
                outputDir,
                dllFileName,
                Path.Combine(outputDir, "Errors"),
                true,  // enableCaching: skips the Roslyn compile when the generated source matches the .hash file
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
