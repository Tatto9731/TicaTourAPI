namespace TicaTourAPI.DTOs.Auth;

public sealed class RegisterTravelerRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public string PreferredLanguage { get; set; } = "es";
    public string PreferredCurrency { get; set; } = "CRC";
    public bool DarkMode { get; set; } = false;

    public string? IdNumber { get; set; }
    public DateTime? BirthDate { get; set; }

    public string[] Preferences { get; set; } = [];
    public bool? RequiresTransport { get; set; }
}