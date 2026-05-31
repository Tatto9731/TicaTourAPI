namespace TicaTourAPI.DTOs.Me;

public sealed class UpdateMyProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string PreferredLanguage { get; set; } = "es";
    public string PreferredCurrency { get; set; } = "CRC";
    public bool DarkMode { get; set; }
}