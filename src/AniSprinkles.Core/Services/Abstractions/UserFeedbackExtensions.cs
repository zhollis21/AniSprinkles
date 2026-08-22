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
    public static Task ShowFailureSnackbarAsync(
        this IUserFeedback feedback,
        Exception exception,
        string fallbackMessage,
        Action? retryAction = null,
        string retryText = "Retry")
    {
        if (exception is AniListApiException { Kind: ApiErrorKind.ServiceOutage } apiEx)
        {
            return feedback.ShowSnackbarAsync(apiEx.UserTitle);
        }

        return retryAction is null
            ? feedback.ShowSnackbarAsync(fallbackMessage)
            : feedback.ShowSnackbarAsync(fallbackMessage, retryText, retryAction);
    }
}
