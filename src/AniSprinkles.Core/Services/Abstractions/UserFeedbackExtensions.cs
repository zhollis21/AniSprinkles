namespace AniSprinkles.Services.Abstractions;

public static class UserFeedbackExtensions
{
    /// <summary>
    /// Shows a failure snackbar that adapts to the exception kind: during a service outage the global
    /// banner is already visible, so the snackbar repeats the outage title and omits the Retry action
    /// (retrying won't work for minutes or hours). All other exception kinds keep the normal retry flow.
    /// <para>
    /// Was duplicated verbatim in four page models before #62 moved them into Core.
    /// </para>
    /// </summary>
    /// <param name="duration">
    /// Overrides the default dwell time. Settings' save-failure snackbar passes 20 seconds because
    /// the Retry is the only recovery path and the user has to notice it (#128). Ignored on the
    /// outage path, which has no action to miss.
    /// </param>
    public static Task ShowFailureSnackbarAsync(
        this IUserFeedback feedback,
        Exception exception,
        string fallbackMessage,
        Action? retryAction = null,
        string retryText = "Retry",
        TimeSpan? duration = null)
    {
        if (exception is AniListApiException { Kind: ApiErrorKind.ServiceOutage } apiEx)
        {
            return feedback.ShowSnackbarAsync(apiEx.UserTitle);
        }

        return retryAction is null
            ? feedback.ShowSnackbarAsync(fallbackMessage)
            : feedback.ShowSnackbarAsync(fallbackMessage, retryText, retryAction, duration);
    }
}
