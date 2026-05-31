namespace TicaTourAPI.DTOs.Company;

public sealed class UpdateCompanyUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string CompanyRole { get; set; } = "staff"; // admin | staff
}