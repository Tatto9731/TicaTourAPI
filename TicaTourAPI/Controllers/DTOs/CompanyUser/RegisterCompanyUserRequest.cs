using TicaTourCompany.Controllers.DTOs.Auth;

namespace TicaTourCompany.Controllers.DTOs.CompanyUser
{
    // Cuerpo combinado para TU flujo de registro
    public record RegisterCompanyUserRequest(
        IdentityUserCreateDto User,
        CompanyUserCreateDto CompanyUser);
}
