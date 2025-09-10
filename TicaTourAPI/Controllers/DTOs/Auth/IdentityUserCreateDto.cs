using System.ComponentModel.DataAnnotations;

namespace TicaTourCompany.Controllers.DTOs.Auth
{
    // Sólo campos mínimos para crear el AspNetUser
    public record IdentityUserCreateDto(
        [EmailAddress, Required] string Email,
        [Required, MinLength(3)] string UserName,
        [Required, MinLength(6)] string Password);
}
