using System.Net;
using System.Text;
using System.Text.Json;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// An <see cref="HttpMessageHandler"/> for <c>AniListClient</c> that both records what was sent and
/// scripts what comes back, keyed off the GraphQL operation name in the request body.
/// <para>
/// Recording the parsed request is the point: the client's entire contract with AniList is the shape
/// of one JSON envelope, and a variable that silently stops being sent (or starts being sent as a
/// literal <c>null</c>, which AniList treats as a filter rather than as "not provided") is invisible
/// from the outside until a list comes back empty in production.
/// </para>
/// </summary>
public sealed class ScriptedGraphQlHandler : HttpMessageHandler
{
    private readonly Func<CapturedGraphQlRequest, HttpResponseMessage> _responder;
    private readonly List<CapturedGraphQlRequest> _requests = [];
    private readonly Lock _gate = new();

    /// <remarks>
    /// The responder runs per request and must build a fresh <see cref="HttpResponseMessage"/> each
    /// time: <c>AniListClient</c> disposes the response it receives, so handing out one shared
    /// instance breaks the second call in any test that retries or pages.
    /// </remarks>
    public ScriptedGraphQlHandler(Func<CapturedGraphQlRequest, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>
    /// A snapshot of what has been sent so far. Deliberately a copy: the caller is free to
    /// enumerate it while the handler goes on appending. The single-value accessors below read the
    /// live list under the lock instead, since cloning it to reach one element buys nothing.
    /// </summary>
    public IReadOnlyList<CapturedGraphQlRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public CapturedGraphQlRequest Last
    {
        get
        {
            lock (_gate)
            {
                return _requests[^1];
            }
        }
    }

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    public int CallsTo(string operationName)
    {
        lock (_gate)
        {
            return _requests.Count(r => string.Equals(r.OperationName, operationName, StringComparison.Ordinal));
        }
    }

    /// <summary>A GraphQL success: <c>{"data": ...}</c>.</summary>
    public static HttpResponseMessage Data(string dataJson)
        => Raw(HttpStatusCode.OK, $$"""{"data": {{dataJson}} }""");

    /// <summary>AniList returns GraphQL errors on HTTP 200 as well as on failures.</summary>
    public static HttpResponseMessage GraphQlError(string message, HttpStatusCode status = HttpStatusCode.OK)
        => Raw(status, JsonSerializer.Serialize(new { errors = new[] { new { message } } }));

    public static HttpResponseMessage Raw(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var captured = new CapturedGraphQlRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body);

        lock (_gate)
        {
            _requests.Add(captured);
        }

        return _responder(captured);
    }
}

/// <summary>One recorded request, with the GraphQL envelope already parsed.</summary>
public sealed class CapturedGraphQlRequest
{
    private readonly JsonElement? _root;

    public CapturedGraphQlRequest(
        HttpMethod method, Uri? uri, string? authScheme, string? bearerToken, string body)
    {
        Method = method;
        Uri = uri;
        AuthScheme = authScheme;
        BearerToken = bearerToken;
        Body = body;

        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                // Cloned so the element outlives the document, which is disposed here rather than
                // held for the life of the handler: JsonDocument.Parse(string) rents from
                // ArrayPool, and a captured request is read long after the request completed. The
                // clone owns its own memory, so this type needs no disposal and no assertion can
                // arrive after a document went away.
                using var document = JsonDocument.Parse(body);
                _root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // A non-JSON body is itself a finding; leave the parsed accessors empty rather than
                // failing every test in the class with a parse error.
            }
        }
    }

    public HttpMethod Method { get; }

    public Uri? Uri { get; }

    public string? AuthScheme { get; }

    public string? BearerToken { get; }

    public string Body { get; }

    public string? Query => Root?.TryGetProperty("query", out var q) == true ? q.GetString() : null;

    public string? OperationName
        => Root?.TryGetProperty("operationName", out var o) == true ? o.GetString() : null;

    /// <summary>The <c>variables</c> object, or <c>null</c> when the operation sent none.</summary>
    public JsonElement? Variables
        => Root?.TryGetProperty("variables", out var v) == true && v.ValueKind == JsonValueKind.Object
            ? v
            : null;

    private JsonElement? Root => _root;

    /// <summary>
    /// True when the variable was sent at all. Distinct from a <c>null</c> value on purpose: an
    /// omitted GraphQL argument matches everything, a literal null filters for null.
    /// </summary>
    public bool HasVariable(string name)
        => Variables?.TryGetProperty(name, out _) == true;

    public JsonElement Variable(string name)
    {
        var variables = Variables ?? throw new InvalidOperationException(
            $"Request for {OperationName} sent no variables object.");

        return variables.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Request for {OperationName} sent no variable named '{name}'. Body: {Body}");
    }

    public string? StringVariable(string name) => Variable(name).GetString();

    public int IntVariable(string name) => Variable(name).GetInt32();

    public bool? BoolVariable(string name)
        => Variable(name).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    public string[] StringArrayVariable(string name)
        => [.. Variable(name).EnumerateArray().Select(e => e.GetString() ?? string.Empty)];
}
