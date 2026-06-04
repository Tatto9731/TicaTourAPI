namespace TicaTourAPI.DTOs.Auth;

public sealed class CompleteSocialProfileRequest
{
    public string? FullName { get; set; }
    public string? Phone { get; set; }

    public string PreferredLanguage { get; set; } = "es";
    public string PreferredCurrency { get; set; } = "CRC";

    public string? IdNumber { get; set; }
    public DateTime? BirthDate { get; set; }

    public string[] Preferences { get; set; } = [];
    public bool? RequiresTransport { get; set; }
}