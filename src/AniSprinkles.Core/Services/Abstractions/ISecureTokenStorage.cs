namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// The persistence seam under <see cref="AniSprinkles.Services.TokenStore"/>. In the app this wraps
/// <c>SecureStorage.Default</c> (the Android keystore); tests substitute a dictionary.
/// <para>
/// Exists so the concurrency contract around the OAuth token — single-flight the first load, publish
/// once, never let a failing read clear state a successful one published — can be tested. Before
/// #119 that logic lived in <c>AuthService</c> alongside <c>WebAuthenticator</c> and
/// <c>Android.Webkit.CookieManager</c>, which pins that class to the MAUI app project that
/// <c>tests/</c> cannot reference.
/// </para>
/// </summary>
public interface ISecureTokenStorage
{
    /// <summary>The stored value for <paramref name="key"/>, or null when absent. May throw.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>. May throw.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Removes <paramref name="key"/>. May throw.</summary>
    void Remove(string key);
}
