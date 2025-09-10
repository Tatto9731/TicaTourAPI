namespace TicaTourCompany.Controllers.DTOs.CompanyUser
{
    // Lo que devuelves (sin relaciones)
    public record CompanyUserResponse(
        string UserId,
        int CompanyId,
        string Name,
        string Address,
        string Description,
        string ImageUrl,
        string PhoneNumber);
}
