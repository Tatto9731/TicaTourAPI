namespace TicaTourAPI.DTOs.Admin;

public sealed class UpdateAdminUserProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string PreferredLanguage { get; set; } = "es";
    public string PreferredCurrency { get; set; } = "CRC";
    public bool DarkMode { get; set; }
    public bool IsIdentityVerified { get; set; }
}