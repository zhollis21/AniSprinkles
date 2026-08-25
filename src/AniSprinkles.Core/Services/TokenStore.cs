using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>What a token lookup found. See <see cref="TokenStore.GetAsync"/>.</summary>
public enum TokenState
{
    /// <summary>Nothing stored, or the stored value could not be read.</summary>
    Absent,

    /// <summary>A token was stored but its expiry has passed. The caller owns the sign-out.</summary>
    Expired,

    /// <summary>A usable token.</summary>
    Valid,
}

/// <summary>The outcome of <see cref="TokenStore.GetAsync"/>.</summary>
/// <param name="State">Which of the three cases this is.</param>
/// <param name="AccessToken">Non-null only when <paramref name="State"/> is <see cref="TokenState.Valid"/>.</param>
/// <param name="ExpiresAt">The expiry that was read, when there was one. Present for the expired case too, for logging.</param>
public readonly record struct TokenLookup(TokenState State, string? AccessToken, DateTimeOffset? ExpiresAt);

/// <summary>
/// Owns the OAuth access token and its expiry: the read from secure storage, the in-memory copy every
/// caller shares, and the expiry check.
/// <para>
/// Split out of <c>AuthService</c> in #119. <c>AuthService</c> keeps the platform pieces —
/// <c>WebAuthenticator</c> and the Android cookie store — and delegates token state here, which is
/// pure logic over an async read and therefore testable.
/// </para>
/// </summary>
public sealed class TokenStore
{
    internal const string TokenKey = "anilist_access_token";
    internal const string TokenExpiryKey = "anilist_access_token_expires_at";

    private readonly ISecureTokenStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenStore> _logger;
    /// <summary>
    /// Serialises the <em>load operation</em>, so the storage read happens at most once however many
    /// callers arrive together.
    /// </summary>
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    /// <summary>
    /// Guards the three fields below — a separate, non-async lock, because sign-in and sign-out
    /// publish state without going through the load at all. Held only across field assignments, never
    /// across an await, so it cannot deadlock the UI thread that <c>SignOutAsync</c> runs on.
    /// </summary>
    private readonly Lock _stateLock = new();

    private string? _accessToken;
    private DateTimeOffset? _expiresAt;

    /// <summary>
    /// Whether the initial read has happened, however it turned out. Separate from
    /// <see cref="_accessToken"/> on purpose: the token is also null for a signed-out user and after
    /// a failed read, so "is the token null?" cannot answer "has the load run?". Gating on the token
    /// instead would let every caller re-read storage for as long as the user stays signed out.
    /// </summary>
    private bool _loaded;

    public TokenStore(ISecureTokenStorage storage, TimeProvider timeProvider, ILogger<TokenStore> logger)
    {
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The current token, loading it from secure storage on first use.
    /// </summary>
    public async Task<TokenLookup> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        // Snapshot both fields together. Reading them one at a time would let a concurrent sign-in
        // or sign-out land in between and produce a lookup describing neither state.
        string? accessToken;
        DateTimeOffset? expiresAt;
        lock (_stateLock)
        {
            accessToken = _accessToken;
            expiresAt = _expiresAt;
        }

        if (accessToken is null)
        {
            return new TokenLookup(TokenState.Absent, null, null);
        }

        if (expiresAt is not null && expiresAt <= _timeProvider.GetUtcNow())
        {
            return new TokenLookup(TokenState.Expired, null, expiresAt);
        }

        return new TokenLookup(TokenState.Valid, accessToken, expiresAt);
    }

    /// <summary>Publishes a freshly obtained token and persists it.</summary>
    public async Task SetAsync(string accessToken, DateTimeOffset? expiresAt)
    {
        lock (_stateLock)
        {
            _accessToken = accessToken;
            _expiresAt = expiresAt;
            _loaded = true; // we know what is stored; nothing left to read
        }

        await _storage.SetAsync(TokenKey, accessToken);
        if (expiresAt is not null)
        {
            await _storage.SetAsync(TokenExpiryKey, expiresAt.Value.ToString("O"));
        }
        else
        {
            _storage.Remove(TokenExpiryKey);
        }
    }

    /// <summary>
    /// Drops the token from memory and from storage. The in-memory clear happens first and cannot
    /// throw, so a caller whose storage removal fails is still left signed out.
    /// </summary>
    public void Clear()
    {
        Forget();
        _storage.Remove(TokenKey);
        _storage.Remove(TokenExpiryKey);
    }

    /// <summary>Drops the token from memory only. Never throws.</summary>
    /// <remarks>
    /// Leaves the store <em>loaded</em>: the caller has decided there is no usable token, and going
    /// back to storage would re-read the value it just rejected. A subsequent sign-in republishes
    /// through <see cref="SetAsync"/>.
    /// </remarks>
    public void Forget()
    {
        lock (_stateLock)
        {
            _accessToken = null;
            _expiresAt = null;
            _loaded = true;
        }
    }

    /// <summary>
    /// Performs the initial read at most once, however many callers arrive at once.
    /// <para>
    /// Single-flighting is what fixes #119. The fallback catch below treats an unreadable token as
    /// "signed out", which is correct for one caller and destructive for two: without the gate, a
    /// caller whose read failed would clear the token a concurrent caller had already published, and
    /// anything sampling auth in between would render a signed-in user as signed out.
    /// </para>
    /// </summary>
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_loaded)
            {
                return;
            }
        }

        // Throws OperationCanceledException for the caller that cancelled, which is correct — it is
        // that caller's own cancellation, and it leaves the load unperformed for whoever comes next.
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            lock (_stateLock)
            {
                if (_loaded)
                {
                    return;
                }
            }

            // Read into locals and publish once, below. Assigning the fields as each read returns
            // would expose a token whose expiry has not been read yet, and IsExpired-style checks
            // treat a null expiry as "not expired" — so a concurrent caller would accept a token
            // this load was about to reject.
            string? accessToken = null;
            DateTimeOffset? expiresAt = null;

            // Secure storage sits on the Android keystore, which can fail for reasons that have
            // nothing to do with us (corrupted keystore, a restored backup, a device-credential
            // change). Those surface as assorted platform exceptions, hence the broad catch. Letting
            // one escape would take the app down: every tab page model reaches this from an async
            // void OnAppearing, where there is nothing to catch it. An unreadable token is
            // functionally the same as an absent one, so fall back to signed-out and let the user
            // sign in again.
            try
            {
                accessToken = await _storage.GetAsync(TokenKey);
                var rawExpiry = await _storage.GetAsync(TokenExpiryKey);
                if (DateTimeOffset.TryParse(rawExpiry, out var expiry))
                {
                    expiresAt = expiry;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is the caller's business, not a storage failure. Filtered so only the
                // CALLER'S cancellation escapes: most callers pass no token and await this from
                // async void lifecycle paths, where an unfiltered rethrow is a crash. Rethrowing
                // before the publish below leaves _loaded false, so the next caller retries.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AUTH token-load failed; treating as signed out.");
                accessToken = null;
                expiresAt = null;
            }

            lock (_stateLock)
            {
                // A sign-in or sign-out can complete while this read is in flight, and what it
                // published is newer than what storage held when the read started. Yielding to it is
                // the same rule the gate enforces between concurrent loads: whoever published first
                // wins, and nothing overwrites a decision already taken.
                if (_loaded)
                {
                    return;
                }

                _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
                _expiresAt = expiresAt;
                _loaded = true;
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }
}
