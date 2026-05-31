namespace TicaTourAPI.DTOs.Leads;

public sealed class CreateLeadRequest
{
    public string? ExperienceSlug { get; set; }
    public string? CompanySlug { get; set; }

    public string TravelerName { get; set; } = string.Empty;
    public string? TravelerEmail { get; set; }
    public string? TravelerPhone { get; set; }

    public string Channel { get; set; } = "Marketplace"; // Marketplace | WhatsApp | Campaign
    public decimal EstimatedValue { get; set; } = 0;
    public string Currency { get; set; } = "CRC";
    public string? Message { get; set; }
}