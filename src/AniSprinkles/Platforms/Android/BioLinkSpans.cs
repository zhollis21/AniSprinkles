using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using System.Runtime.CompilerServices;
using AniSprinkles.Converters;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using Sentry;
using AndroidColor = Android.Graphics.Color;
using AndroidView = Android.Views.View;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// Makes the Markdown links in character and staff bios visible and tappable (#137).
/// <para>
/// <c>Label.TextType="Html"</c> renders through <c>Html.FromHtml</c>, which turns every anchor into a
/// <see cref="URLSpan"/>. A <see cref="URLSpan"/> ignores the label's <c>TextColor</c> and paints
/// itself with the TextView's <c>textColorLink</c> instead — which resolves through
/// <c>Theme.MaterialComponents.DayNight</c> to <c>colorAccent</c>, which <c>colors.xml</c> sets to
/// transparent so the rainbow theming can show through. The result was link text painted transparent
/// while still taking up its own width: names vanished, leaving gaps.
/// </para>
/// <para>
/// Rather than overriding the theme attribute — which would mean either a project-wide
/// <c>styles.xml</c> or giving up the transparent accent — each link is swapped for a span that
/// paints its own colour. That sidesteps <c>textColorLink</c> entirely, and lets each link take a
/// colour keyed on its own text so a given name always paints the same one.
/// </para>
/// </summary>
internal static class BioLinkSpans
{
    // Which views already carry a watcher. The handler mapper can run its customization more than
    // once for the same view, and a second watcher would mean a second Apply per text change.
    private static readonly ConditionalWeakTable<TextView, BioLinkTextWatcher> Watched = new();

    /// <summary>
    /// Applies the link spans now, and keeps applying them as the text changes.
    /// <para>
    /// The watcher is the load-bearing half. This runs from a handler mapping registered under a
    /// custom key — the only kind that isn't shadowed by Controls' own Label mapper — and a custom
    /// key runs when the handler is built, not on every text change. The bio text changes after
    /// that: toggling spoilers rewrites <c>BioProse</c>, and without the watcher the rebuilt
    /// <c>Spanned</c> would arrive with plain <see cref="URLSpan"/>s and the links would go
    /// invisible again on the first toggle.
    /// </para>
    /// </summary>
    public static void Attach(TextView textView)
    {
        if (!Watched.TryGetValue(textView, out _))
        {
            var watcher = new BioLinkTextWatcher(textView);
            Watched.Add(textView, watcher);
            textView.AddTextChangedListener(watcher);
        }

        Apply(textView);
    }

    /// <summary>
    /// Replaces every <see cref="URLSpan"/> in the view's formatted text with a
    /// <see cref="BioLinkSpan"/>. Idempotent: once swapped there are no <see cref="URLSpan"/>s left,
    /// so a repeat call does nothing. That matters because the handler mapping runs again whenever
    /// the text changes — which the spoiler toggle does.
    /// </summary>
    public static void Apply(TextView textView)
    {
        if (textView.TextFormatted is not ISpanned formatted)
        {
            return;
        }

        var linkClass = Java.Lang.Class.FromType(typeof(URLSpan));
        if (formatted.GetSpans(0, formatted.Length(), linkClass) is not { Length: > 0 })
        {
            return;
        }

        // Html.FromHtml hands back a SpannedString, which is immutable — the spans have to be
        // edited on a mutable copy and set back.
        var spannable = new SpannableString(formatted);
        var text = spannable.ToString() ?? string.Empty;
        var replaced = 0;

        foreach (var candidate in spannable.GetSpans(0, spannable.Length(), linkClass) ?? [])
        {
            if (candidate is not URLSpan urlSpan)
            {
                continue;
            }

            var start = spannable.GetSpanStart(urlSpan);
            var end = spannable.GetSpanEnd(urlSpan);
            var flags = spannable.GetSpanFlags(urlSpan);
            var url = urlSpan.URL;

            spannable.RemoveSpan(urlSpan);

            if (start < 0 || end > text.Length || end <= start)
            {
                continue;
            }

            spannable.SetSpan(new BioLinkSpan(url, ColorFor(text[start..end])), start, end, flags);
            replaced++;
        }

        textView.TextFormatted = spannable;

        // Deliberately not LinkMovementMethod: it derives from ScrollingMovementMethod, so it makes
        // the TextView itself scrollable and swallows vertical drags that belong to the page's
        // ScrollView. BioLinkMovementMethod consumes a touch only when it actually lands on a link.
        textView.MovementMethod = BioLinkMovementMethod.Instance;

        // Only fires on labels that actually carry links, so it is a handful of lines per bio
        // rather than per label. Worth keeping: a silent no-op here is exactly how this shipped
        // broken the first time, and the count is the difference between "never ran" and "ran but
        // the spans were overwritten afterwards".
        IPlatformApplication.Current?.Services.GetService<ILogger<BioLinkSpan>>()?
            .LogInformation("BIOLINK replaced {Count} link span(s)", replaced);
    }

    private static AndroidColor ColorFor(string linkText)
    {
        // Same deterministic hash the rest of the app's rainbow accents use, so a character's name
        // paints the same colour here as its card does elsewhere.
        var key = RainbowAccentConverter.ResourceKeyFor(linkText);

        if (MauiApplication.Current?.Resources.TryGetValue(key, out var resource) == true
            && resource is MauiColor color)
        {
            return color.ToPlatform();
        }

        // Resources missing is not a real runtime state, but painting an invisible link is exactly
        // the bug being fixed, so fall back to something legible rather than to the theme.
        return AndroidColor.ParseColor("#FF6B9D");
    }
}

/// <summary>
/// Re-applies the link spans whenever the label's text is rebuilt — which the spoiler toggle does.
/// </summary>
internal sealed class BioLinkTextWatcher(TextView textView) : Java.Lang.Object, ITextWatcher
{
    private bool _applying;

    public void AfterTextChanged(IEditable? s)
    {
        // Apply sets TextFormatted, which lands back here. The second pass finds no URLSpans left
        // and would stop anyway; the guard keeps it to one pass rather than relying on that.
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            BioLinkSpans.Apply(textView);
        }
        finally
        {
            _applying = false;
        }
    }

    public void BeforeTextChanged(Java.Lang.ICharSequence? s, int start, int count, int after)
    {
    }

    public void OnTextChanged(Java.Lang.ICharSequence? s, int start, int before, int count)
    {
    }
}

/// <summary>
/// A bio link: paints itself, and routes its tap in-app when it points at an entity this app has a
/// page for.
/// </summary>
internal sealed class BioLinkSpan(string? url, AndroidColor color) : ClickableSpan
{
    public override void UpdateDrawState(TextPaint ds)
    {
        ds.Color = color;

        // The rainbow colour already reads as interactive, and AniList bios link names densely
        // enough that underlining every one turns the paragraph into a rule.
        ds.UnderlineText = false;
    }

    public override void OnClick(AndroidView widget)
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        // ILogger<T> is the canonical path per AGENTS.md; resolved from the container rather than
        // injected because a span is constructed by the handler mapper, not by DI.
        var logger = services.GetService<ILogger<BioLinkSpan>>();

        // Discarding a Task that swallows its own exceptions — the click arrives on the UI thread,
        // which is where both GoToAsync and the browser launch need to be, so there is nothing to
        // marshal and nothing for a caller to await.
        _ = FollowAsync(services, logger);
    }

    private async Task FollowAsync(IServiceProvider services, ILogger? logger)
    {
        try
        {
            var target = AniListLinkTarget.Resolve(url);
            if (target is not null)
            {
                var navigation = services.GetService<INavigationService>();
                if (navigation is null)
                {
                    return;
                }

                // A bio link is a navigation entry point that exists nowhere else in the app, so
                // it is worth tracing: "how did they reach this character page" is otherwise
                // unanswerable from a crash report.
                logger?.LogInformation(
                    "NAVTRACE BioLink → {Route} with id={EntityId}", target.Route, target.Id);
                SentrySdk.AddBreadcrumb(
                    $"Follow bio link ({target.Route} {target.Id})", "navigation", "user");

                await navigation.GoToAsync(
                    target.Route,
                    animate: false,
                    new Dictionary<string, object> { [target.ParameterName] = target.Id });
                return;
            }

            // Manga is ours, but the details page is anime-only. Say what the rest of the app says
            // rather than sending it to the browser, so the answer to "can I open manga" doesn't
            // depend on where the link was tapped (#12).
            if (AniListLinkTarget.IsUnsupportedEntity(url))
            {
                logger?.LogInformation("NAVTRACE BioLink → skipped unsupported entity {Url}", url);

                if (services.GetService<IUserFeedback>() is { } feedback)
                {
                    await feedback.ShowToastAsync("Manga & Novel details aren't supported yet.");
                }

                return;
            }

            // Staff bios link out to agency, social and personal sites; those are the browser's job.
            if (services.GetService<IExternalBrowser>() is { } browser
                && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Host only: enough to tell an AniList entry from a social link when reading a
                // trace, without putting the full URL in the breadcrumb buffer.
                SentrySdk.AddBreadcrumb($"Open bio link externally ({uri.Host})", "navigation", "user");
                await browser.OpenAsync(uri);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to follow bio link {Url}", url);
        }
    }
}

/// <summary>
/// Touch handling for <see cref="BioLinkSpan"/>. Exists because the obvious choice,
/// <c>LinkMovementMethod</c>, extends <c>ScrollingMovementMethod</c> and would make the bio label
/// steal vertical drags from the page's ScrollView. This one returns <see langword="false"/> for
/// anything that isn't a tap on a link, leaving the gesture to the parent.
/// </summary>
internal sealed class BioLinkMovementMethod : BaseMovementMethod
{
    public static BioLinkMovementMethod Instance { get; } = new();

    public override bool OnTouchEvent(TextView? widget, ISpannable? buffer, MotionEvent? e)
    {
        if (widget is null || buffer is null || e is null)
        {
            return false;
        }

        if (e.Action is not (MotionEventActions.Up or MotionEventActions.Down))
        {
            return false;
        }

        if (widget.Layout is not { } layout)
        {
            return false;
        }

        var x = e.GetX() - widget.TotalPaddingLeft + widget.ScrollX;
        var y = e.GetY() - widget.TotalPaddingTop + widget.ScrollY;

        var line = layout.GetLineForVertical((int)y);

        // GetOffsetForHorizontal snaps to the nearest character even when the touch is well past the
        // end of the line, so without this a tap in the blank space beside a short line would count
        // as hitting whatever link happens to end there.
        if (x < layout.GetLineLeft(line) || x > layout.GetLineRight(line))
        {
            return false;
        }

        var offset = layout.GetOffsetForHorizontal(line, x);
        var spans = buffer.GetSpans(offset, offset, Java.Lang.Class.FromType(typeof(ClickableSpan)));
        if (spans is not { Length: > 0 } || spans[0] is not ClickableSpan span)
        {
            return false;
        }

        if (e.Action == MotionEventActions.Up)
        {
            span.OnClick(widget);
        }

        // Both DOWN and UP get consumed on a hit: returning false for DOWN would hand the whole
        // gesture to the ScrollView and the UP would never arrive here.
        return true;
    }
}
