namespace AniSprinkles.Utilities;

/// <summary>
/// The rule for a setting the user has changed locally but the server has not confirmed yet (#128).
/// <para>
/// Generalises what <c>c4a2830</c> built for <c>DisplayAdultContent</c> alone. A save can fail
/// outright, or still be in flight, and either way the next viewer response reports the <em>old</em>
/// value — so assigning it blind reverts the user's explicit choice with no explanation. The
/// invariant is that a local change outranks the server's copy until the server agrees with it.
/// </para>
/// </summary>
public static class PendingValue
{
    /// <summary>
    /// The value a caller should hold given what the server just reported: the server's, unless a
    /// local change is still awaiting confirmation and the server disagrees with it.
    /// </summary>
    /// <param name="awaitingUpstream">
    /// Cleared exactly when the server reports the value being held, whichever response brought it —
    /// the reply to our own save, or a later load once it landed. Deliberately <em>not</em> cleared
    /// on every response: a fresh load is not a confirmation, it just asks a server that may still
    /// be behind us, or may never have received the save at all.
    /// </param>
    /// <param name="serverValue">What the viewer response says.</param>
    /// <param name="localValue">What this device is currently showing.</param>
    public static T Resolve<T>(ref bool awaitingUpstream, T serverValue, T localValue)
    {
        if (awaitingUpstream && !EqualityComparer<T>.Default.Equals(serverValue, localValue))
        {
            // Still behind. Keep the local choice and stay pending.
            return localValue;
        }

        awaitingUpstream = false;
        return serverValue;
    }
}
