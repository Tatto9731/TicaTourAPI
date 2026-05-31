namespace TicaTourAPI.DTOs.Notifications;

public sealed class CreateNotificationRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "system";
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
}