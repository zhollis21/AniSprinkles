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

    [Test]
    public void Redact_ReplacesABearerTokenEmbeddedInFreeText()
    {
        var result = SensitiveText.Redact("Invalid token: Bearer " + Jwt);

        Assert.That(result, Is.EqualTo("Invalid token: " + SensitiveText.RedactedBearer));
        Assert.That(result, Does.Not.Contain(Jwt));
    }

    [Test]
    public void Redact_KeepsTheSurroundingTextSoTheErrorStaysReadable()
    {
        // The message is what the user sees on the error panel and what a maintainer reads in the
        // log; blanking the whole string would trade one problem for another.
        var result = SensitiveText.Redact("AniList request failed (400). Bearer " + Jwt + " was rejected.");

        Assert.That(result, Does.StartWith("AniList request failed (400). "));
        Assert.That(result, Does.EndWith(" was rejected."));
    }

    [TestCase("bearer ")]
    [TestCase("BEARER ")]
    [TestCase("Bearer\t")]
    [TestCase("Bearer  ")]
    public void Redact_IsNotFooledByCasingOrWhitespace(string prefix)
    {
        Assert.That(SensitiveText.Redact(prefix + Jwt), Does.Not.Contain(Jwt));
    }

    [TestCase("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9.dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")]
    // standard base64 — + and / , with = padding.
    [TestCase("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXo+P0AjJCVeJiooKV8rfDo8Pg==")]
    [TestCase("a+b/c+d/e==")]
    public void Redact_CoversBothBase64Alphabets(string token)
    {
        // The character class carries -_ and +/ deliberately, so a token is scrubbed whichever
        // encoding it arrives in. Narrowing it to one alphabet would leak the other into the log
        // file and Sentry, which is the whole point of #124.
        var result = SensitiveText.Redact("Rejected: Bearer " + token);

        Assert.That(result, Does.Not.Contain(token));
        Assert.That(result, Is.EqualTo("Rejected: " + SensitiveText.RedactedBearer));
    }

    [Test]
    public void Redact_LeavesTextWithNoTokenAlone()
    {
        const string message = "AniList is temporarily disabled for maintenance.";

        Assert.That(SensitiveText.Redact(message), Is.EqualTo(message));
    }

    [Test]
    public void Redact_PassesNullThroughSoOptionalMessagesNeedNoNullCheck()
    {
        Assert.That(SensitiveText.Redact(null), Is.Null);
    }
}

/// <summary>
/// The vector the issue was filed for: <c>AniListClient</c> embeds up to 500 characters of the raw
/// response body into the exception message, so an auth-failure body echoing the credential put it
/// in the file log, in logcat and in Sentry.
/// </summary>
public class AniListClientRedactionTests
{
    [Test]
    public async Task AGraphQlErrorBodyCarryingTheToken_DoesNotPutItInTheExceptionMessage()
    {
        // The 4xx branch where the body parses as GraphQL, so apiMessage becomes the message.
        var client = NewClient(Respond(
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"message\":\"Invalid token: Bearer " + SensitiveTextTests.Jwt + "\"}]}"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.CurrentContext.CancellationToken));

        Assert.That(ex.Message, Does.Not.Contain(SensitiveTextTests.Jwt));
        Assert.That(ex.Message, Does.Contain(SensitiveText.RedactedBearer));

        // Classification runs on the raw text, so redaction must not cost the "Invalid token" match
        // that turns a 400 into a re-sign-in prompt rather than a generic failure.
        Assert.That(ex.Kind, Is.EqualTo(ApiErrorKind.Authentication));
    }

    [Test]
    public async Task ANonJsonErrorBodyCarryingTheToken_IsRedactedInTheFallbackToo()
    {
        // The body does not parse as GraphQL, so the message is built from the 500-character
        // `fallback` slice of the raw response instead — a separate path from the one above.
        var client = NewClient(Respond(
            HttpStatusCode.BadRequest,
            "<html><body>Rejected credential Bearer " + SensitiveTextTests.Jwt + "</body></html>"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.CurrentContext.CancellationToken));

        Assert.That(ex.Message, Does.Not.Contain(SensitiveTextTests.Jwt));
        Assert.That(ex.Message, Does.Contain(SensitiveText.RedactedBearer));
    }

    [Test]
    public async Task AGraphQlErrorReturnedOnHttp200_IsRedactedAsWell()
    {
        // AniList returns GraphQL errors with a 200 as well. This path was missed in the original
        // write-up of #124 and reaches the exception message the same way the 4xx branch does.
        var client = NewClient(Respond(
            HttpStatusCode.OK,
            "{\"errors\":[{\"message\":\"Unauthorized - Bearer " + SensitiveTextTests.Jwt + "\"}]}"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.CurrentContext.CancellationToken));

        Assert.That(ex.Message, Does.Not.Contain(SensitiveTextTests.Jwt));
        Assert.That(ex.Kind, Is.EqualTo(ApiErrorKind.Authentication));
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
    [Test]
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

        Assert.That(scrubbed.SentryExceptions!, Has.Exactly(1).Items);
        var exception = scrubbed.SentryExceptions!.Single();
        Assert.That(exception.Value, Does.Not.Contain(SensitiveTextTests.Jwt));

        // Grouping keys off the type and the frames. Redacting those would scatter one issue across
        // as many Sentry issues as there are distinct tokens.
        Assert.That(exception.Type, Is.EqualTo("AniListApiException"));
        Assert.That(exception.Stacktrace, Is.Not.Null);
    }

    [Test]
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

        Assert.That(scrubbed.Message!.Formatted, Does.Not.Contain(SensitiveTextTests.Jwt));

        // The template carries no user data and is what Sentry groups similar messages by.
        Assert.That(scrubbed.Message.Message, Is.EqualTo("Auth failed: {0}"));
    }

    [Test]
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

        Assert.That(scrubbed.SentryExceptions!, Has.Exactly(1).Items);
        var exception = scrubbed.SentryExceptions!.Single();
        Assert.That(exception.Value, Does.Not.Contain(SensitiveTextTests.Jwt));
    }

    [Test]
    public void Scrub_HandlesAnEventWithNeitherExceptionsNorAMessage()
    {
        // Sentry sends plain events too; the scrubber runs on every one of them.
        var evt = new SentryEvent();

        Assert.That(SentryScrubber.Scrub(evt), Is.SameAs(evt));
    }
}

public class ErrorReportServiceTests
{
    [Test]
    public void Record_ReturnsDetailsWithNoRawToken()
    {
        // Belt-and-braces: anything arriving from AniListClient is already redacted, but Record is
        // also called with exceptions that never went through it.
        var service = new ErrorReportService(NullLogger<ErrorReportService>.Instance);

        var details = service.Record(
            new InvalidOperationException("Rejected Bearer " + SensitiveTextTests.Jwt),
            "Load media details");

        Assert.That(details, Does.Not.Contain(SensitiveTextTests.Jwt));
        Assert.That(details, Does.Contain("Load media details"));
    }
}
