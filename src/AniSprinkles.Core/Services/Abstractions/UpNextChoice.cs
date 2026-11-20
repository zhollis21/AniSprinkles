namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// What the user picked in an up-next offer (#103). Each title carries its own
/// <see cref="UpNextCandidate.Target"/>, so the picks are the whole answer.
/// </summary>
public sealed record UpNextChoice(IReadOnlyList<UpNextCandidate> Selected);
