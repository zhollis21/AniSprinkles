using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Views;

/// <summary>
/// Centered card for editing an entry's episode progress from the long-press action menu. Offers
/// −/+ steppers, direct numeric entry, and (when the total is known) a slider — all kept in sync.
/// Closes with the chosen progress as a boxed <see cref="int"/>, or <c>null</c> on Cancel/dismiss.
/// The caller decides whether reaching the total triggers the completion flow.
/// </summary>
public partial class EditProgressPopup : Popup<object>
{
    private readonly int? _max;
    private int _value;
    private bool _suppress;

    public EditProgressPopup(string animeTitle, int currentProgress, int? maxEpisodes)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(animeTitle))
        {
            TitleLabel.Text = animeTitle;
        }

        _max = maxEpisodes is > 0 ? maxEpisodes : null;
        _value = Clamp(currentProgress);

        _suppress = true;
        ProgressEntry.Text = _value.ToString();
        if (_max is { } max)
        {
            ProgressSlider.Maximum = max;
            ProgressSlider.Value = _value;
            ProgressSlider.IsVisible = true;
            MaxCaption.Text = $"of {max} episode{(max == 1 ? string.Empty : "s")}";
            MaxCaption.IsVisible = true;
        }
        _suppress = false;
    }

    private int Clamp(int value)
    {
        if (value < 0)
        {
            value = 0;
        }

        if (_max is { } max && value > max)
        {
            value = max;
        }

        return value;
    }

    /// <summary>Sets the canonical value and mirrors it to the entry/slider without re-triggering handlers.</summary>
    private void SetValue(int value, bool updateEntry)
    {
        _value = Clamp(value);
        _suppress = true;
        if (updateEntry)
        {
            ProgressEntry.Text = _value.ToString();
        }

        if (_max is not null)
        {
            ProgressSlider.Value = _value;
        }
        _suppress = false;
    }

    private void OnDecrementClicked(object? sender, EventArgs e) => SetValue(_value - 1, updateEntry: true);

    private void OnIncrementClicked(object? sender, EventArgs e) => SetValue(_value + 1, updateEntry: true);

    private void OnProgressTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        // Empty field mid-edit: treat as 0 but leave the box empty so the user can retype.
        if (string.IsNullOrEmpty(e.NewTextValue))
        {
            _value = 0;
            if (_max is not null)
            {
                _suppress = true;
                ProgressSlider.Value = 0;
                _suppress = false;
            }
            return;
        }

        // Non-numeric (paste, etc.): revert to the last valid value.
        if (!int.TryParse(e.NewTextValue, out var parsed))
        {
            SetValue(_value, updateEntry: true);
            return;
        }

        var clamped = Clamp(parsed);
        if (clamped != parsed)
        {
            // Typed past the bounds — snap the box back to the clamped value.
            SetValue(clamped, updateEntry: true);
        }
        else
        {
            // In range — update the value and slider but don't fight the caret by rewriting the text.
            SetValue(clamped, updateEntry: false);
        }
    }

    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        var rounded = (int)Math.Round(e.NewValue);
        if (Math.Abs(e.NewValue - rounded) > 0.01)
        {
            _suppress = true;
            ProgressSlider.Value = rounded;
            _suppress = false;
        }

        SetValue(rounded, updateEntry: true);
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null!);

    private async void OnSaveClicked(object? sender, EventArgs e) => await CloseAsync(_value);
}
