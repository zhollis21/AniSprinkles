namespace AniSprinkles.Pages;

public partial class OAuthWebViewPage : ContentPage
{
    private readonly TaskCompletionSource<IDictionary<string, string>?> _tcs;
    private readonly string _callbackUri;
    private bool _dismissed;

    public OAuthWebViewPage(Uri authorizeUri, string callbackUri, TaskCompletionSource<IDictionary<string, string>?> tcs)
        : this(new UrlWebViewSource { Url = authorizeUri.ToString() }, callbackUri, tcs)
    {
    }

    private OAuthWebViewPage(WebViewSource source, string callbackUri, TaskCompletionSource<IDictionary<string, string>?> tcs)
    {
        InitializeComponent();
        _tcs = tcs;
        _callbackUri = callbackUri;
        AuthWebView.Source = source;
    }

#if CI
    public static OAuthWebViewPage CreateCiMock(string callbackUri, TaskCompletionSource<IDictionary<string, string>?> tcs)
    {
        const string token = "ci-stub-token";
        const int expiresInSeconds = 3600;
        const int redirectDelayMilliseconds = 2000;
        var redirectUri = $"{callbackUri}#access_token={token}&expires_in={expiresInSeconds}";
        var html = $$"""
            <!doctype html>
            <html>
            <head>
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: grid;
                        place-items: center;
                        background: #17171A;
                        color: #E6E6E6;
                        font-family: sans-serif;
                    }
                    a {
                        color: #5DBBFF;
                    }
                </style>
            </head>
            <body>
                <main>
                    <p>Completing CI sign-in...</p>
                    <p><a href="{{redirectUri.Replace("&", "&amp;", StringComparison.Ordinal)}}">Continue</a></p>
                </main>
                <script>
                    setTimeout(function () {
                        window.location.href = '{{redirectUri}}';
                    }, {{redirectDelayMilliseconds}});
                </script>
            </body>
            </html>
            """;

        return new OAuthWebViewPage(new HtmlWebViewSource { Html = html }, callbackUri, tcs);
    }
#endif

    private async void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        // Match the exact redirect URI or redirect URI + fragment/query to avoid accepting
        // URLs like anisprinkles://auth.evil/ that share a prefix with our callback.
        if (!e.Url.Equals(_callbackUri, StringComparison.OrdinalIgnoreCase) &&
            !e.Url.StartsWith(_callbackUri + "#", StringComparison.OrdinalIgnoreCase) &&
            !e.Url.StartsWith(_callbackUri + "?", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // OAuth redirect — cancel WebView navigation and extract the token from the fragment.
        // AniList uses the implicit flow: anisprinkles://auth#access_token=...&expires_in=...
        e.Cancel = true;

        await DismissModalAsync(
            TryParseFragmentProperties(e.Url, out var properties)
                ? properties
                : null);
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = DismissModalAsync(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Safety net: ensure the TCS resolves if the page is dismissed by any other means.
        if (!_dismissed)
        {
            _tcs.TrySetResult(null);
        }
    }

    private async Task DismissModalAsync(IDictionary<string, string>? properties)
    {
        // Guard against double-pop: both OnNavigating and OnBackButtonPressed can call this,
        // and a back press while a redirect navigation is in-flight could trigger both.
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await Navigation.PopModalAsync(animated: true);
                }
                catch (Exception)
                {
                    // Modal may already be dismissed (e.g. system back gesture during redirect).
                }
            });
        }
        catch (Exception)
        {
            // The caller is waiting for this task to resolve; report the auth outcome even
            // if the modal is already gone or the UI dispatcher is no longer accepting work.
        }

        _tcs.TrySetResult(properties);
    }

    private static bool TryParseFragmentProperties(string url, out IDictionary<string, string>? properties)
    {
        properties = null;

        var fragmentIndex = url.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? url[(fragmentIndex + 1)..] : string.Empty;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            try
            {
                var key = Uri.UnescapeDataString(pair[..separatorIndex]);
                var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);

                if (!result.TryAdd(key, value))
                {
                    // Duplicate keys in an OAuth redirect are invalid — treat as failure.
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        properties = result;
        return true;
    }
}
