using AniSprinkles.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles.Utilities;

/// <summary>
/// The platform shim for <see cref="DiagnosticsSessionFlusher"/> (#112) — nothing but a null-tolerant
/// way to reach it from places that have no constructor to inject into.
/// <para>
/// Static and defensively resolved because its callers are the three global exception handlers and
/// the activity's pause and resume: places where DI may not be wired yet, and where an exception
/// escaping would become the crash instead of explaining one. The decision-making lives in Core,
/// where it is testable.
/// </para>
/// </summary>
public static class DiagnosticsFlush
{
    /// <summary>Writes the ring out. Safe to call from anywhere, including a handler firing during
    /// startup before DI exists.</summary>
    public static void Flush() => Run(flusher => flusher.Flush());

    /// <summary>Drops a flush this process wrote, once it is clear the process survived.</summary>
    public static void ClearIfOwnedByThisProcess() => Run(flusher => flusher.ClearIfOwnedByThisProcess());

    private static void Run(Func<DiagnosticsSessionFlusher, bool> action)
    {
        try
        {
            // Not ServiceProviderHelper.GetServiceProvider: that throws when DI is not yet wired,
            // which is a reasonable contract for page creation and the wrong one for a crash handler
            // that may fire before the container exists.
            var services = Application.Current?.Handler?.MauiContext?.Services
                ?? IPlatformApplication.Current?.Services;

            if (services?.GetService<DiagnosticsSessionFlusher>() is { } flusher)
            {
                action(flusher);
            }
        }
        catch
        {
            // Deliberately silent and deliberately total. Every caller is a crash handler or a
            // lifecycle callback; there is nowhere useful for an exception to go from here, and
            // logging it would only write into the buffer this just failed to persist.
        }
    }
}
