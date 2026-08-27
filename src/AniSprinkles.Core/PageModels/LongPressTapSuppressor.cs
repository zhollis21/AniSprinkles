namespace AniSprinkles.PageModels;

/// <summary>
/// The tap-suppression window that follows a long press. MAUI's TapGestureRecognizer still fires on
/// finger-up after a long press, so a card's navigate command would run underneath the action sheet
/// the long press just opened. Navigate commands call <see cref="ShouldSuppressTap"/> first.
/// <para>
/// The Android gesture plumbing that stamps this lives in <c>AniSprinkles.Views.CollectionViewLongPress</c>,
/// in the MAUI app project. Only the timestamp lives here, so page models can be unit-tested without it.
/// </para>
/// </summary>
public static class LongPressTapSuppressor
{
    /// <summary>
    /// How long after a long press a tap is treated as its synthetic follow-up rather than a real tap.
    /// </summary>
    public static readonly TimeSpan SuppressionWindow = TimeSpan.FromMilliseconds(800);

    private static long _lastLongPressTicks;

    /// <summary>
    /// Records that a long press just happened. Called at gesture detection and again at finger-up:
    /// the synthetic tap fires at UP, which can be arbitrarily long after detection (the user can keep
    /// holding), so stamping only at detection would let a slow release outlive the window.
    /// </summary>
    public static void Stamp() => Interlocked.Exchange(ref _lastLongPressTicks, Environment.TickCount64);

    /// <summary>True when a long press fired within <see cref="SuppressionWindow"/>.</summary>
    public static bool ShouldSuppressTap()
        => IsWithinWindow(Environment.TickCount64, Interlocked.Read(ref _lastLongPressTicks));

    /// <summary>
    /// The window comparison itself, as a pure function of two tick counts.
    /// </summary>
    /// <remarks>
    /// Split out so the boundary behaviour can be tested without stamping the shared static. The
    /// stamp is process-wide and lives for <see cref="SuppressionWindow"/>, so a test that set it
    /// could suppress a navigate command in any test class running in parallel.
    /// </remarks>
    internal static bool IsWithinWindow(long nowTicks, long lastLongPressTicks)
        => nowTicks - lastLongPressTicks < SuppressionWindow.TotalMilliseconds;

    /// <summary>Clears the stamp, so a test that sets it cannot leak into a parallel test class.</summary>
    internal static void Reset() => Interlocked.Exchange(ref _lastLongPressTicks, 0);
}
