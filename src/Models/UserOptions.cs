namespace AniSprinkles.Models;

public class UserOptions
{
    public UserTitleLanguage TitleLanguage { get; set; }
    public bool DisplayAdultContent { get; set; }
    public bool AiringNotifications { get; set; }
    public string ProfileColor { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public int ActivityMergeTime { get; set; }
    public UserStaffNameLanguage StaffNameLanguage { get; set; }
    public bool RestrictMessagesToFollowing { get; set; }
    public List<NotificationOption> NotificationOptions { get; set; } = [];
}
