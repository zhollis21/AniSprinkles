namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> whose response for each attempt is produced by a
/// caller-supplied responder (keyed by zero-based attempt index). Records how many times it was
/// invoked. The async responder makes it easy to gate requests open for serialization tests.
/// </summary>
public sealed class QueuedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<int, Task<HttpResponseMessage>> _responder;
    private int _callCount;

    public QueuedHttpMessageHandler(Func<int, Task<HttpResponseMessage>> responder) => _responder = responder;

    public QueuedHttpMessageHandler(Func<int, HttpResponseMessage> responder)
        : this(i => Task.FromResult(responder(i)))
    {
    }

    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>
    /// The last request that actually reached the wire. Handlers above may rebuild the message
    /// rather than forward it (<c>AniListRateLimitHandler</c> does, so a 429 can be retried), so
    /// "what the caller constructed" and "what the inner handler received" are not the same object.
    /// </summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var index = Interlocked.Increment(ref _callCount) - 1;
        return _responder(index);
    }
}
