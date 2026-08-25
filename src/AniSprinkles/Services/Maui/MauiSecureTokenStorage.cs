using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="ISecureTokenStorage"/> over <c>SecureStorage.Default</c> — the Android keystore.
/// <para>
/// Lives here rather than in Core for the same reason as the other adapters in this folder:
/// <c>SecureStorage</c> is MAUI Essentials, whose <c>Get</c>/<c>Set</c>/<c>Remove</c> throw
/// <c>NotImplementedInReferenceAssemblyException</c> on the plain <c>net10.0</c> TFM the tests build
/// against. Keeping it behind the seam is what lets <see cref="TokenStore"/> be tested at all (#119).
/// </para>
/// </summary>
public sealed class MauiSecureTokenStorage : ISecureTokenStorage
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
