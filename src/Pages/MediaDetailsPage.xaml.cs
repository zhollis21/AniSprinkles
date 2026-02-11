namespace AniSprinkles.Pages;

public partial class MediaDetailsPage : ContentPage, IQueryAttributable
{
    private MediaDetailsPageModel ViewModel { get; }

    public MediaDetailsPage()
        : this(ResolveViewModel())
    {
    }

    public MediaDetailsPage(MediaDetailsPageModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = ViewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = Shell.Current.GoToAsync("..");
        return true;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var mediaId = 0;
        if (query.TryGetValue("mediaId", out var rawId))
        {
            if (rawId is int id)
            {
                mediaId = id;
            }
            else if (rawId is string text && int.TryParse(text, out var parsed))
            {
                mediaId = parsed;
            }
        }

        MediaListEntry? entry = null;
        if (query.TryGetValue("listEntry", out var rawEntry) && rawEntry is MediaListEntry castEntry)
        {
            entry = castEntry;
            if (mediaId == 0)
            {
                mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
            }
        }

        _ = ViewModel.LoadAsync(mediaId, entry);
    }

    private static MediaDetailsPageModel ResolveViewModel()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services is null)
        {
            throw new InvalidOperationException("Service provider not available.");
        }

        return services.GetRequiredService<MediaDetailsPageModel>();
    }
}
