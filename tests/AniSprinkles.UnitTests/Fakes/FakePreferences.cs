namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// A dictionary-backed <see cref="IPreferences"/> for the <c>AppSettings.Storage</c> seam (#121).
/// <para>
/// Deliberately stores the boxed value rather than a serialized string, so a test that writes a
/// <c>bool</c> and reads back an <c>int</c> fails loudly instead of round-tripping through
/// <c>ToString</c> and quietly passing — the real platform stores are typed too.
/// </para>
/// </summary>
public sealed class FakePreferences : IPreferences
{
    private readonly Dictionary<string, object?> _values = [];

    /// <summary>Counts writes, for tests asserting that a change actually persisted.</summary>
    public int SetCount { get; private set; }

    public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);

    public void Remove(string key, string? sharedName = null) => _values.Remove(key);

    public void Clear(string? sharedName = null) => _values.Clear();

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
        => _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        _values[key] = value;
        SetCount++;
    }
}
