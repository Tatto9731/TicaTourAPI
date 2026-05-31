namespace TicaTourAPI.DTOs.Company;

public sealed class UpdatePromotionRequest
{
    public string Badge { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DiscountPercent { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = "Scheduled"; // Active | Scheduled | Expired | Paused
}