namespace TicaTourAPI.DTOs.Auth;

public sealed class RegisterTravelerRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PreferredLanguage { get; set; } = "es";
    public string PreferredCurrency { get; set; } = "CRC";
}