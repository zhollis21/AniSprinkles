using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Views;

public partial class ConfirmPopup : Popup<bool>
{
    public ConfirmPopup(
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel",
        bool isDestructive = false,
        string? iconGlyph = null)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        MessageLabel.Text = message;
        CancelButton.Text = cancelText;
        ConfirmButton.Text = confirmText;

        var confirmAccent = isDestructive
            ? (Color)Application.Current!.Resources["RainbowRed"]
            : (Color)Application.Current!.Resources["RainbowGreen"];
        ConfirmButton.TextColor = confirmAccent;
        ConfirmButton.BorderColor = confirmAccent;
        ConfirmButton.BorderWidth = 1;

        if (!string.IsNullOrEmpty(iconGlyph))
        {
            IconSource.Glyph = iconGlyph;
            IconSource.Color = confirmAccent;
            IconImage.IsVisible = true;
        }
    }

    /// <summary>
    /// Shows the popup on the current Shell page and returns the user's choice.
    /// Returns <c>true</c> when the user taps the confirm button, <c>false</c> when
    /// they tap cancel or (when allowed) tap outside the popup.
    /// </summary>
    public static async Task<bool> ShowAsync(
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel",
        bool isDestructive = false,
        string? iconGlyph = null,
        bool canDismissByTappingOutside = true,
        CancellationToken cancellationToken = default)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            return false;
        }

        var popup = new ConfirmPopup(title, message, confirmText, cancelText, isDestructive, iconGlyph);
        var options = new PopupOptions
        {
            Shape = null,
            Shadow = null,
            CanBeDismissedByTappingOutsideOfPopup = canDismissByTappingOutside,
        };

        var result = await page.ShowPopupAsync<bool>(popup, options, cancellationToken);
        return !result.WasDismissedByTappingOutsideOfPopup && result.Result;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CloseAsync(false);
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        await CloseAsync(true);
    }
}
