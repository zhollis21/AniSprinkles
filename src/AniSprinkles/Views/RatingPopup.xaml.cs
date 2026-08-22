using AniSprinkles.Utilities;
using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Views;

public partial class RatingPopup : Popup<object>
{
    private readonly ScoreFormat _scoreFormat;
    private readonly double _maxScore;
    private double _selectedScore;

    public RatingPopup(string? animeTitle = null, double? initialScore = null)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(animeTitle))
        {
            TitleLabel.Text = animeTitle;
        }

        _scoreFormat = AppSettings.ScoreFormat;
        _maxScore = _scoreFormat switch
        {
            ScoreFormat.Point100 => 100,
            _ => 10,
        };

        // Pre-populate when the caller has a non-zero score; otherwise fall back to the
        // per-format empty/default state (0 = "no score" across all formats).
        var initial = initialScore is > 0 ? initialScore.Value : 0;

        switch (_scoreFormat)
        {
            case ScoreFormat.Point5:
                StarsLayout.IsVisible = true;
                if (initial > 0)
                {
                    _selectedScore = Math.Clamp((int)Math.Round(initial), 0, 5);
                    UpdateStarVisuals();
                }
                break;

            case ScoreFormat.Point3:
                SmileysLayout.IsVisible = true;
                if (initial > 0)
                {
                    _selectedScore = Math.Clamp((int)Math.Round(initial), 0, 3);
                    UpdateSmileyVisuals();
                }
                break;

            default: // Point100, Point10, Point10Decimal
                SliderLayout.IsVisible = true;
                ScoreSlider.Maximum = _maxScore;
                // 0 = no score yet (label renders as "Not rated"); pre-populate with the
                // user's existing score otherwise. Slider Minimum is 0 so any in-range
                // existing value round-trips without being clamped.
                var start = initial > 0
                    ? Math.Clamp(initial, ScoreSlider.Minimum, _maxScore)
                    : 0;
                ScoreSlider.Value = start;
                _selectedScore = start;
                UpdateSliderLabel(start);
                break;
        }
    }

    // ── Stars ────────────────────────────────────────────────────────

    private void OnStarTapped(object? sender, EventArgs e)
    {
        if (sender is not Image img ||
            img.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault() is not { } tap ||
            tap.CommandParameter is not string paramStr ||
            !int.TryParse(paramStr, out var stars))
        {
            return;
        }

        _selectedScore = (int)_selectedScore == stars ? 0 : stars;
        UpdateStarVisuals();
    }

    private void UpdateStarVisuals()
    {
        var rating = (int)_selectedScore;
        var accentColor = GetRainbowAccentColor();
        var dimColor = GetResourceColor("Gray500");

        FontImageSource[] icons = [Star1Icon, Star2Icon, Star3Icon, Star4Icon, Star5Icon];
        for (var i = 0; i < icons.Length; i++)
        {
            icons[i].Color = i < rating ? accentColor : dimColor;
        }
    }

    // ── Smileys ──────────────────────────────────────────────────────

    private void OnSmileyTapped(object? sender, EventArgs e)
    {
        if (sender is not Image img ||
            img.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault() is not { } tap ||
            tap.CommandParameter is not string paramStr ||
            !int.TryParse(paramStr, out var rating))
        {
            return;
        }

        _selectedScore = (int)_selectedScore == rating ? 0 : rating;
        UpdateSmileyVisuals();
    }

    private void UpdateSmileyVisuals()
    {
        var rating = (int)_selectedScore;
        SmileyHappy.Opacity = rating == 3 ? 1.0 : 0.4;
        SmileyNeutral.Opacity = rating == 2 ? 1.0 : 0.4;
        SmileySad.Opacity = rating == 1 ? 1.0 : 0.4;
    }

    // ── Slider ───────────────────────────────────────────────────────

    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        var rounded = _scoreFormat == ScoreFormat.Point10Decimal
            ? Math.Round(e.NewValue * 2, MidpointRounding.AwayFromZero) / 2.0
            : Math.Round(e.NewValue);

        if (Math.Abs(e.NewValue - rounded) > 0.01)
        {
            ScoreSlider.Value = rounded;
            return;
        }

        _selectedScore = rounded;
        UpdateSliderLabel(rounded);
    }

    private void UpdateSliderLabel(double score)
    {
        // 0 in AniList means "no score recorded" — surface that explicitly so dragging
        // the slider to the bottom doesn't read as "rated this 0/10".
        if (score <= 0)
        {
            SliderLabel.Text = "Not rated";
            return;
        }

        SliderLabel.Text = _scoreFormat == ScoreFormat.Point10Decimal
            ? $"{score:0.0} / {_maxScore:0}"
            : $"{score:0} / {_maxScore:0}";
    }

    // ── Actions ──────────────────────────────────────────────────────

    private async void OnSkipClicked(object? sender, EventArgs e)
    {
        await CloseAsync(null!);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        await CloseAsync(_selectedScore);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Color GetRainbowAccentColor()
    {
        if (Application.Current?.Resources.TryGetValue("RainbowCyan", out var cyan) == true && cyan is Color c)
        {
            return c;
        }

        return Colors.Cyan;
    }

    private static Color GetResourceColor(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c)
        {
            return c;
        }

        return Colors.Gray;
    }
}
