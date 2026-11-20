using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// The up-next settings (#103). Device-local, unlike everything else on this page: they write
/// straight to <see cref="UpNextPreferences"/> and stay out of <c>HasUnsavedChanges</c> and the
/// debounced AniList save, which has no field for them.
/// </summary>
public partial class SettingsPageModel
{
    private bool _loadingUpNext;

    /// <summary>The Up Next section's own card, open by default like Display Preferences.</summary>
    [ObservableProperty]
    private bool _isUpNextExpanded = true;

    [RelayCommand]
    private void ToggleUpNext() => IsUpNextExpanded = !IsUpNextExpanded;

    /// <summary>Whether completing something offers its sequels and side stories.</summary>
    [ObservableProperty]
    private bool _upNextEnabled;

    /// <summary>"Current" or "Planning" — the chip converters compare by name.</summary>
    [ObservableProperty]
    private string _upNextStatus = nameof(MediaListStatus.Current);

    private void LoadUpNextPreferences()
    {
        _loadingUpNext = true;
        try
        {
            UpNextEnabled = UpNextPreferences.IsEnabled(_preferences);
            UpNextStatus = UpNextPreferences.DefaultStatus(_preferences).ToString();
        }
        finally
        {
            _loadingUpNext = false;
        }
    }

    partial void OnUpNextEnabledChanged(bool value)
    {
        if (!_loadingUpNext)
        {
            UpNextPreferences.SetEnabled(_preferences, value);
            // Logged as well as breadcrumbed: breadcrumbs only reach Sentry with a crash, and the device
            // log is what a "why didn't it offer anything" report is read from.
            _logger.LogInformation("UPNEXT suggestions {State}", value ? "on" : "off");
            SentrySdk.AddBreadcrumb($"Up next suggestions {(value ? "on" : "off")}", "settings", "user");
        }
    }

    [RelayCommand]
    private void SetUpNextStatus(string value)
    {
        if (Enum.TryParse<MediaListStatus>(value, out var status)
            && status is MediaListStatus.Current or MediaListStatus.Planning
            && value == status.ToString())
        {
            UpNextPreferences.SetDefaultStatus(_preferences, status);
            UpNextStatus = value;
            _logger.LogInformation("UPNEXT suggestions go to {Status}", status);
            SentrySdk.AddBreadcrumb($"Up next adds to {status}", "settings", "user");
        }
    }
}
