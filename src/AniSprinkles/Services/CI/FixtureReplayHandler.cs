#if CI
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AniSprinkles.Services.Fixtures;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// Answers AniList requests from recorded fixtures instead of the network (#134).
/// <para>
/// This sits at the bottom of the HTTP pipeline, where <c>FaultInjectingHttpHandler</c> already
/// proved a synthetic response is indistinguishable from a real one to everything above. That
/// placement is the whole point: the real <c>AniListClient</c>, <c>CachingAniListClient</c>,
/// <c>AniListRateLimitHandler</c> and <c>AniListErrorClassifier</c> all run for real in CI, against
/// bytes AniList actually sent. The <c>CIAniListClient</c> this replaces sat *above* all of them, so
/// none of that code was exercised and a mapping bug could not fail a CI run.
/// </para>
/// <para>
/// Reads come from <see cref="FixtureStore"/>. Writes are synthesized here, because a recording of a
/// mutation would be a recording of a change made to a real account — see <see cref="MutationState"/>.
/// </para>
/// </summary>
internal sealed class FixtureReplayHandler : HttpMessageHandler
{
    private readonly FixtureStore _fixtures;
    private readonly MutationState _mutations;
    private readonly ILogger<FixtureReplayHandler> _logger;

    public FixtureReplayHandler(
        FixtureStore fixtures, MutationState mutations, ILogger<FixtureReplayHandler> logger)
    {
        _fixtures = fixtures;
        _mutations = mutations;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var parsed = body is null ? null : JsonNode.Parse(body)?.AsObject();
        var operationName = parsed?["operationName"]?.GetValue<string>();

        if (operationName is null)
        {
            return Miss("<no operationName>", "the request carried no GraphQL operation name");
        }

        var variables = parsed?["variables"]?.DeepClone();

        // Mutations first: they are never recorded, so a fixture lookup would always miss.
        if (_mutations.TryAnswer(operationName, variables, _fixtures, out var synthesized))
        {
            return Ok(synthesized);
        }

        var query = parsed?["query"]?.GetValue<string>();
        var fingerprint = query is null ? null : GraphQlFixtureKey.QueryFingerprint(query);
        var key = GraphQlFixtureKey.Derive(operationName, variables);

        if (!_fixtures.TryGet(key, fingerprint, out var fixture))
        {
            // Distinguishing the two cases matters, because the fix differs. Nothing at this address
            // means the app is asking something never recorded — a new sort, a deeper page. Something
            // at the address but under a different query means the query itself changed, and serving
            // the old body would answer a new question with old fields and quietly render nulls.
            return Miss(
                key,
                _fixtures.HasAny(key)
                    ? $"the {operationName} query has changed since these fixtures were recorded. "
                      + "Re-record with tools/record-anilist-fixtures.cs."
                    : $"no recording for {operationName}. The app is asking for something the "
                      + "fixtures do not cover — a new sort, or a page deeper than was captured. "
                      + "Re-record with tools/record-anilist-fixtures.cs.");
        }

        var response = fixture.Response?.DeepClone();
        if (response is null)
        {
            return Miss(key, "the fixture has no response body");
        }

        AiringTimeRebaser.Rebase(response, fixture.RecordedAt);
        AdultCanary.Splice(operationName, variables, response);
        _mutations.Apply(operationName, response);

        return Ok(response);
    }

    private HttpResponseMessage Ok(JsonNode response)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// A request the fixtures cannot answer.
    /// <para>
    /// Deliberately loud and deliberately a failure. The stubs this replaces answered anything they
    /// did not model with an empty list, so a details-page sort silently emptied its section and no
    /// screenshot changed — issue #134 in one sentence. A miss must be impossible to mistake for
    /// data, so it logs at Error with a greppable marker and returns a 5xx the app surfaces.
    /// </para>
    /// </summary>
    private HttpResponseMessage Miss(string key, string why)
    {
        _logger.LogError("FIXTURE MISS {Key} — {Why}", key, why);

        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                $$"""{"errors":[{"message":"FIXTURE MISS {{key}} - {{why}}"}]}""",
                Encoding.UTF8,
                "application/json"),
        };
    }
}
#endif
