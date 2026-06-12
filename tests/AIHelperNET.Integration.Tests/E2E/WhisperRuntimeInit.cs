using System.Runtime.CompilerServices;
using Whisper.net.LibraryLoader;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Forces Whisper.net to use the CPU runtime for the entire test process. The Vulkan GPU
/// backend loads on machines with vulkan-1.dll present but fails at CreateBuilder() when no
/// supported GPU is available (e.g. CI / this dev box), and Whisper.net has no automatic
/// fallback. Production keeps its configured (Vulkan-first) order; tests pin CPU for reliability.
/// </summary>
internal static class WhisperRuntimeInit
{
    [ModuleInitializer]
    internal static void Init() => RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
}
