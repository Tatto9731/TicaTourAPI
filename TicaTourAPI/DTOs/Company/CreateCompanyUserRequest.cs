namespace TicaTourAPI.DTOs.Company;

public sealed class CreateCompanyUserRequest
{
    public string CompanySlug { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string CompanyRole { get; set; } = "staff"; // admin | staff
}