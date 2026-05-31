namespace TicaTourAPI.DTOs.Campaigns;

public sealed class UpdateCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public string Placement { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public string Currency { get; set; } = "CRC";
    public string Status { get; set; } = "Scheduled";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}