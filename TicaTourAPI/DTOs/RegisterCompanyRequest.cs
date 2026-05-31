namespace TicaTourAPI.DTOs.Auth;

public sealed class RegisterCompanyRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string CompanySlug { get; set; } = string.Empty;
    public string? CompanyDescription { get; set; }
    public string Province { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string CompanyEmail { get; set; } = string.Empty;
    public string? Whatsapp { get; set; }
    public string? WebsiteUrl { get; set; }
}