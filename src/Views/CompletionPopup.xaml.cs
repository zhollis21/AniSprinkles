using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Views;

public partial class CompletionPopup : Popup<bool>
{
    public CompletionPopup(string animeTitle, int totalEpisodes)
    {
        InitializeComponent();
        DescriptionLabel.Text = $"You've watched all {totalEpisodes} episodes of {animeTitle}. Mark as Completed?";
    }

    private async void OnNoClicked(object? sender, EventArgs e)
    {
        await CloseAsync(false);
    }

    private async void OnYesClicked(object? sender, EventArgs e)
    {
        await CloseAsync(true);
    }
}
