namespace AniSprinkles.Pages;

public partial class OAuthWebViewPage : ContentPage
{
    private readonly TaskCompletionSource<IDictionary<string, string>?> _tcs;
    private readonly string _callbackUri;
    private bool _dismissed;

    public OAuthWebViewPage(Uri authorizeUri, string callbackUri, TaskCompletionSource<IDictionary<string, string>?> tcs)
    {
        InitializeComponent();
        _tcs = tcs;
        _callbackUri = callbackUri;
        AuthWebView.Source = authorizeUri.ToString();
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
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

        if (TryParseFragmentProperties(e.Url, out var properties))
        {
            _tcs.TrySetResult(properties);
        }
        else
        {
            _tcs.TrySetResult(null);
        }

        DismissModal();
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
    }

    protected override bool OnBackButtonPressed()
    {
        _tcs.TrySetResult(null);
        DismissModal();
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Safety net: ensure the TCS resolves if the page is dismissed by any other means.
        _tcs.TrySetResult(null);
    }

    private void DismissModal()
    {
        // Guard against double-pop: both OnNavigating and OnBackButtonPressed can call this,
        // and a back press while a redirect navigation is in-flight could trigger both.
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        MainThread.BeginInvokeOnMainThread(async () =>
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
            catch (UriFormatException)
            {
                return false;
            }
        }

        properties = result;
        return true;
    }
}
