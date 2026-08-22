namespace AniSprinkles.Models;

public class UpdateUserRequest
{
    public UserTitleLanguage? TitleLanguage { get; set; }
    public bool? DisplayAdultContent { get; set; }
    public bool? AiringNotifications { get; set; }
    public ScoreFormat? ScoreFormat { get; set; }
    public string? ProfileColor { get; set; }
    public UserStaffNameLanguage? StaffNameLanguage { get; set; }
    public bool? RestrictMessagesToFollowing { get; set; }
    public int? ActivityMergeTime { get; set; }
    public List<NotificationOptionInput>? NotificationOptions { get; set; }
}

public class NotificationOptionInput
{
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
