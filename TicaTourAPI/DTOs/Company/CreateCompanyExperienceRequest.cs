namespace TicaTourAPI.DTOs.Company;

public sealed class CreateCompanyExperienceRequest
{
    public string CompanySlug { get; set; } = string.Empty;
    public string PublicCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string CategorySlug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceCurrency { get; set; } = "CRC";
    public int DurationMinutes { get; set; }
    public string DurationLabel { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public string? MainImageUrl { get; set; }
    public string[] Tags { get; set; } = [];
    public string? NextSlotLabel { get; set; }
}