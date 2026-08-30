#if DEBUG
namespace AniSprinkles.Services.FaultInjection;

/// <summary>
/// Which seam an armed profile fires at (#125). The two seams test genuinely different things and
/// arming both from one profile would fire it twice for a single logical call, so a profile picks
/// exactly one.
/// <list type="bullet">
/// <item><see cref="Client"/> — <see cref="FaultInjectingAniListClient"/>, above the HTTP pipeline.
/// Composes with the CI fixtures, so a real screen can load and the <em>next</em> call can break.</item>
/// <item><see cref="Http"/> — <see cref="FaultInjectingHttpHandler"/>, inside the pipeline, so
/// <c>AniListRateLimitHandler</c>, <c>LoggingHandler</c>, <c>AniListClient.SendAsync</c>'s
/// retry-once and <c>AniListErrorClassifier</c> all run for real. Needs the real client.</item>
/// </list>
/// </summary>
public enum FaultLayer
{
    Client,
    Http,
}

/// <summary>
/// Which matching calls an armed fault applies to. Deterministic by construction — a random fault
/// you saw once is one you cannot re-run, so there is no random scope here. If chaos/soak testing
/// ever justifies one, seed it then and log the seed.
/// </summary>
public enum FaultScopeKind
{
    /// <summary>Only the next matching call.</summary>
    Next,

    /// <summary>Every <c>N</c>th matching call (the 3rd, 6th, 9th … for N=3).</summary>
    EveryNth,

    /// <summary>The first <c>N</c> matching calls, then pass through.</summary>
    FirstN,

    /// <summary>Every matching call, until cleared.</summary>
    Always,
}

/// <param name="Kind">Which calls are affected.</param>
/// <param name="N">The count for <see cref="FaultScopeKind.EveryNth"/> / <see cref="FaultScopeKind.FirstN"/>; unused otherwise.</param>
public readonly record struct FaultScope(FaultScopeKind Kind, int N = 0)
{
    public static FaultScope Next => new(FaultScopeKind.Next);
    public static FaultScope Always => new(FaultScopeKind.Always);
    public static FaultScope EveryNth(int n) => new(FaultScopeKind.EveryNth, n);
    public static FaultScope FirstN(int n) => new(FaultScopeKind.FirstN, n);

    /// <summary>
    /// Whether the <paramref name="hit"/>-th matching call (1-based) is affected. A non-positive
    /// <see cref="N"/> matches nothing rather than throwing: the count arrives from an adb extra, and
    /// a typo there should disarm the fault, not crash the app it was armed against.
    /// </summary>
    public bool Includes(int hit) => Kind switch
    {
        FaultScopeKind.Next => hit == 1,
        FaultScopeKind.FirstN => N > 0 && hit <= N,
        FaultScopeKind.EveryNth => N > 0 && hit % N == 0,
        FaultScopeKind.Always => true,
        _ => false,
    };
}

/// <summary>
/// An armed fault. Disarmed is represented by the absence of one (a null <see cref="FaultState.Current"/>),
/// so a build carrying this machinery behaves exactly like a build without it until something arms it.
/// </summary>
/// <param name="OperationPrefix">
/// Case-insensitive prefix match against the operation name, or null for any operation.
/// <para>
/// <b>What "the operation name" is depends on the layer.</b> At <see cref="FaultLayer.Client"/> it is
/// the <c>IAniListClient</c> method name, so prefix matching lets <c>--es op GetStudio</c> arm
/// <c>GetStudioAsync</c>. At <see cref="FaultLayer.Http"/> it is the GraphQL <c>operationName</c> from
/// the request body — <c>Studio</c>, <c>Media</c>, <c>MediaCharactersPage</c> — because that is all
/// the handler can see.
/// </para>
/// <para>
/// The two do not reduce to one another. Stripping <c>Get</c>/<c>Load</c>/<c>Async</c> would line up
/// <c>GetStudioAsync</c> with <c>Studio</c>, but not <c>GetMediaListAsync</c> with
/// <c>MediaListCollection</c> or <c>SearchMediaPageAsync</c> with <c>Search</c> — so no normalisation
/// is attempted, and <c>FaultInjectingHttpHandler</c> logs the operation it actually saw when an
/// armed prefix misses.
/// </para>
/// </param>
/// <param name="Kind">
/// The failure to throw, or null to delay without failing. <see cref="Delay"/> is the only way to
/// open the cancellation and interlock windows that #116's defect family lives in, and those need
/// the call to <em>succeed</em> slowly — hence a fault with no error kind is a legitimate profile.
/// </param>
/// <param name="Scope">Which matching calls are affected.</param>
/// <param name="Delay">
/// Latency applied to an affected call, before it fails (if it fails at all).
/// <para>
/// One deliberate exception, at <see cref="FaultLayer.Http"/> with
/// <see cref="ApiErrorKind.RateLimited"/>: there this becomes the synthetic <c>Retry-After</c> rather
/// than latency, because a real 429 comes back promptly and the <em>waiting</em> is
/// <c>AniListRateLimitHandler</c>'s job — which is the thing being rehearsed. It is what lets both
/// branches of that handler be reached: under its 5 s <c>maxAutoRetryWait</c> it auto-retries, over
/// it surfaces <see cref="ApiErrorKind.RateLimited"/> to the user.
/// </para>
/// </param>
/// <param name="Layer">Which seam fires this profile.</param>
/// <param name="AsGraphQlError">
/// At <see cref="FaultLayer.Http"/>, answer with HTTP 200 carrying a GraphQL <c>errors</c> array
/// instead of an HTTP status code. AniList genuinely reports many failures this way, and it is a
/// separate branch in <c>AniListClient.SendAsyncCore</c> routing through
/// <c>AniListErrorClassifier.ClassifyGraphQlError</c> rather than <c>ClassifyHttpError</c>. Nothing
/// could reach it on device before this. Ignored at <see cref="FaultLayer.Client"/>, which throws
/// the classified exception directly and never has a response to shape.
/// </param>
public sealed record FaultProfile(
    string? OperationPrefix,
    ApiErrorKind? Kind,
    FaultScope Scope,
    TimeSpan Delay,
    FaultLayer Layer,
    bool AsGraphQlError = false)
{
    public bool Matches(string operation)
        => OperationPrefix is null
        || operation.StartsWith(OperationPrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What should happen to one call. <see cref="FaultDecision.None"/> means "pass through".</summary>
public readonly record struct FaultDecision(TimeSpan Delay, ApiErrorKind? Kind, bool AsGraphQlError = false)
{
    public static FaultDecision None => default;

    public bool IsPassThrough => Delay <= TimeSpan.Zero && Kind is null;
}
#endif
