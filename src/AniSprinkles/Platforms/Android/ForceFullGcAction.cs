#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Core;

namespace AniSprinkles;

/// <summary>
/// A full garbage collection on both runtimes, callable from MAUI DevFlow, so a memory reading
/// taken afterwards shows what is really still referenced (#205).
/// <para>
/// <c>am dumpheap</c> and Android Studio's "force GC" collect only the Java heap. A MAUI view's
/// native peer is held by a global reference from its .NET object, so the Java GC cannot free it
/// until a .NET collection has released that reference first. A Java-only GC therefore leaves every
/// discarded list cell in the <c>dumpsys meminfo</c> <c>Views</c> count whether or not anything
/// still uses it, and a cell that is merely uncollected looks exactly like a leaked one.
/// </para>
/// <para>
/// The order is .NET, then Java, twice: the first .NET pass queues finalizers and drops global
/// references, the Java pass collects what those references held, and the second round catches
/// objects that the first round's finalizers released.
/// </para>
/// <para>
/// Invoke it with the <c>maui-devflow</c> MCP server's <c>maui_invoke_action</c> tool
/// (<c>maui_list_actions</c> shows it); the <c>maui devflow</c> CLI has no verb for app actions. Then
/// read <c>Views:</c> from <c>dumpsys meminfo com.RainbowSprinkles.AniSprinkles</c>.
/// </para>
/// <para>
/// Debug only: the DevFlow package reference carries the same condition, so this type cannot exist
/// in a Release build.
/// </para>
/// </summary>
public static class ForceFullGcAction
{
    [DevFlowAction("force-full-gc", Description = "Full .NET + Java garbage collection, for memory readings (#205)")]
    public static string Run()
    {
        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var grefsBefore = Java.Interop.JniEnvironment.Runtime.GlobalReferenceCount;

        for (var round = 0; round < 2; round++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            Java.Lang.JavaSystem.Gc();
            Java.Lang.JavaSystem.RunFinalization();
        }

        var managedAfter = GC.GetTotalMemory(forceFullCollection: false);
        var grefsAfter = Java.Interop.JniEnvironment.Runtime.GlobalReferenceCount;

        return $"managed {managedBefore / 1024} KB -> {managedAfter / 1024} KB, "
            + $"global refs {grefsBefore} -> {grefsAfter}";
    }
}
#endif
