namespace AniSprinkles.Pages;

public partial class OAuthWebViewPage : ContentPage
{
    private readonly TaskCompletionSource<IDictionary<string, string>?> _tcs;
    private readonly string _callbackScheme;

    public OAuthWebViewPage(Uri authorizeUri, string callbackScheme, TaskCompletionSource<IDictionary<string, string>?> tcs)
    {
        InitializeComponent();
        _tcs = tcs;
        _callbackScheme = callbackScheme;
        AuthWebView.Source = authorizeUri.ToString();
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith(_callbackScheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // OAuth redirect — cancel WebView navigation and extract the token from the fragment.
        // AniList uses the implicit flow: anisprinkles://auth#access_token=...&expires_in=...
        e.Cancel = true;

        var fragmentIndex = e.Url.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? e.Url[(fragmentIndex + 1)..] : string.Empty;

        var properties = fragment
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

        _tcs.TrySetResult(properties);

        MainThread.BeginInvokeOnMainThread(async () =>
            await Navigation.PopModalAsync(animated: true));
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
    }

    protected override bool OnBackButtonPressed()
    {
        _tcs.TrySetResult(null);
        MainThread.BeginInvokeOnMainThread(async () =>
            await Navigation.PopModalAsync(animated: true));
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Safety net: ensure the TCS resolves if the page is dismissed by any other means.
        _tcs.TrySetResult(null);
    }
}
