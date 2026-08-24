using System.Net;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sentry.Protocol;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #124. Redaction used to happen only on the string handed back to the on-screen error panel — the
/// least sensitive sink, on the user's own device — while the rotating file log and Sentry received
/// the raw exception. These cover the fix at its source: the token never enters the exception
/// message, so every downstream consumer is safe by construction.
/// </summary>
public class SensitiveTextTests
{
    // A structurally real JWT: three base64url segments, so the character class actually gets
    // exercised (dots, dashes and underscores included) rather than a token that happens to be
    // plain alphanumeric.
    public const string Jwt =
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9.dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    public void Redact_ReplacesABearerTokenEmbeddedInFreeText()
    {
        var result = SensitiveText.Redact("Invalid token: Bearer " + Jwt);

        Assert.Equal("Invalid token: " + SensitiveText.RedactedBearer, result);
        Assert.DoesNotContain(Jwt, result);
    }

    [Fact]
    public void Redact_KeepsTheSurroundingTextSoTheErrorStaysReadable()
    {
        // The message is what the user sees on the error panel and what a maintainer reads in the
        // log; blanking the whole string would trade one problem for another.
        var result = SensitiveText.Redact("AniList request failed (400). Bearer " + Jwt + " was rejected.");

        Assert.StartsWith("AniList request failed (400). ", result);
        Assert.EndsWith(" was rejected.", result);
    }

    [Theory]
    [InlineData("bearer ")]
    [InlineData("BEARER ")]
    [InlineData("Bearer\t")]
    [InlineData("Bearer  ")]
    public void Redact_IsNotFooledByCasingOrWhitespace(string prefix)
    {
        Assert.DoesNotContain(Jwt, SensitiveText.Redact(prefix + Jwt));
    }

    [Theory]
    // base64url — what a JWT actually uses ( - and _ , no padding).
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9.dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")]
    // standard base64 — + and / , with = padding.
    [InlineData("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXo+P0AjJCVeJiooKV8rfDo8Pg==")]
    [InlineData("a+b/c+d/e==")]
    public void Redact_CoversBothBase64Alphabets(string token)
    {
        // The character class carries -_ and +/ deliberately, so a token is scrubbed whichever
        // encoding it arrives in. Narrowing it to one alphabet would leak the other into the log
        // file and Sentry, which is the whole point of #124.
        var result = SensitiveText.Redact("Rejected: Bearer " + token);

        Assert.DoesNotContain(token, result);
        Assert.Equal("Rejected: " + SensitiveText.RedactedBearer, result);
    }

    [Fact]
    public void Redact_LeavesTextWithNoTokenAlone()
    {
        const string message = "AniList is temporarily disabled for maintenance.";

        Assert.Equal(message, SensitiveText.Redact(message));
    }

    [Fact]
    public void Redact_PassesNullThroughSoOptionalMessagesNeedNoNullCheck()
    {
        Assert.Null(SensitiveText.Redact(null));
    }
}

/// <summary>
/// The vector the issue was filed for: <c>AniListClient</c> embeds up to 500 characters of the raw
/// response body into the exception message, so an auth-failure body echoing the credential put it
/// in the file log, in logcat and in Sentry.
/// </summary>
public class AniListClientRedactionTests
{
    [Fact]
    public async Task AGraphQlErrorBodyCarryingTheToken_DoesNotPutItInTheExceptionMessage()
    {
        // The 4xx branch where the body parses as GraphQL, so apiMessage becomes the message.
        var client = NewClient(Respond(
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"message\":\"Invalid token: Bearer " + SensitiveTextTests.Jwt + "\"}]}"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(SensitiveTextTests.Jwt, ex.Message);
        Assert.Contains(SensitiveText.RedactedBearer, ex.Message);

        // Classification runs on the raw text, so redaction must not cost the "Invalid token" match
        // that turns a 400 into a re-sign-in prompt rather than a generic failure.
        Assert.Equal(ApiErrorKind.Authentication, ex.Kind);
    }

    [Fact]
    public async Task ANonJsonErrorBodyCarryingTheToken_IsRedactedInTheFallbackToo()
    {
        // The body does not parse as GraphQL, so the message is built from the 500-character
        // `fallback` slice of the raw response instead — a separate path from the one above.
        var client = NewClient(Respond(
            HttpStatusCode.BadRequest,
            "<html><body>Rejected credential Bearer " + SensitiveTextTests.Jwt + "</body></html>"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(SensitiveTextTests.Jwt, ex.Message);
        Assert.Contains(SensitiveText.RedactedBearer, ex.Message);
    }

    [Fact]
    public async Task AGraphQlErrorReturnedOnHttp200_IsRedactedAsWell()
    {
        // AniList returns GraphQL errors with a 200 as well. This path was missed in the original
        // write-up of #124 and reaches the exception message the same way the 4xx branch does.
        var client = NewClient(Respond(
            HttpStatusCode.OK,
            "{\"errors\":[{\"message\":\"Unauthorized - Bearer " + SensitiveTextTests.Jwt + "\"}]}"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(SensitiveTextTests.Jwt, ex.Message);
        Assert.Equal(ApiErrorKind.Authentication, ex.Kind);
    }

    // Authentication and Unknown are both retried once (#79), so the responder has to answer every
    // attempt rather than only the first.
    private static QueuedHttpMessageHandler Respond(HttpStatusCode status, string body)
        => new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    private static AniListClient NewClient(HttpMessageHandler handler)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        return new AniListClient(
            new HttpClient(handler),
            auth,
            Substitute.For<IOutageStateService>(),
            NullLogger<AniListClient>.Instance);
    }
}

/// <summary>
/// The Sentry-side backstop. Unhandled exceptions reach Sentry without passing through
/// <c>ErrorReportService</c> or <c>AniListClient</c>, so <c>BeforeSend</c> is what covers a throw
/// site that does not exist yet.
/// </summary>
public class SentryScrubberTests
{
    [Fact]
    public void Scrub_RedactsTheExceptionValueButLeavesTheTypeAndStackAlone()
    {
        var evt = new SentryEvent
        {
            SentryExceptions =
            [
                new SentryException
                {
                    Type = "AniListApiException",
                    Value = "Invalid token: Bearer " + SensitiveTextTests.Jwt,
                    Stacktrace = new SentryStackTrace(),
                }
            ],
        };

        var scrubbed = SentryScrubber.Scrub(evt);

        var exception = Assert.Single(scrubbed.SentryExceptions!);
        Assert.DoesNotContain(SensitiveTextTests.Jwt, exception.Value);

        // Grouping keys off the type and the frames. Redacting those would scatter one issue across
        // as many Sentry issues as there are distinct tokens.
        Assert.Equal("AniListApiException", exception.Type);
        Assert.NotNull(exception.Stacktrace);
    }

    [Fact]
    public void Scrub_RedactsTheEventMessageWithoutLosingItsTemplate()
    {
        var evt = new SentryEvent
        {
            Message = new SentryMessage
            {
                Message = "Auth failed: {0}",
                Formatted = "Auth failed: Bearer " + SensitiveTextTests.Jwt,
            },
        };

        var scrubbed = SentryScrubber.Scrub(evt);

        Assert.DoesNotContain(SensitiveTextTests.Jwt, scrubbed.Message!.Formatted);

        // The template carries no user data and is what Sentry groups similar messages by.
        Assert.Equal("Auth failed: {0}", scrubbed.Message.Message);
    }

    [Fact]
    public void Scrub_RedactsALazilyProjectedExceptionSequence()
    {
        // SentryExceptions is typed as IEnumerable. A deferred sequence yields fresh objects on
        // every enumeration, so mutating inside a foreach would redact copies that are then thrown
        // away — and Sentry would still send the token. The scrubber has to materialize and assign
        // back, which a test using a plain List cannot distinguish.
        var evt = new SentryEvent
        {
            SentryExceptions = new[] { "Invalid token: Bearer " + SensitiveTextTests.Jwt }
                .Select(value => new SentryException { Type = "AniListApiException", Value = value }),
        };

        var scrubbed = SentryScrubber.Scrub(evt);

        var exception = Assert.Single(scrubbed.SentryExceptions!);
        Assert.DoesNotContain(SensitiveTextTests.Jwt, exception.Value);
    }

    [Fact]
    public void Scrub_HandlesAnEventWithNeitherExceptionsNorAMessage()
    {
        // Sentry sends plain events too; the scrubber runs on every one of them.
        var evt = new SentryEvent();

        Assert.Same(evt, SentryScrubber.Scrub(evt));
    }
}

public class ErrorReportServiceTests
{
    [Fact]
    public void Record_ReturnsDetailsWithNoRawToken()
    {
        // Belt-and-braces: anything arriving from AniListClient is already redacted, but Record is
        // also called with exceptions that never went through it.
        var service = new ErrorReportService(NullLogger<ErrorReportService>.Instance);

        var details = service.Record(
            new InvalidOperationException("Rejected Bearer " + SensitiveTextTests.Jwt),
            "Load media details");

        Assert.DoesNotContain(SensitiveTextTests.Jwt, details);
        Assert.Contains("Load media details", details);
    }
}
