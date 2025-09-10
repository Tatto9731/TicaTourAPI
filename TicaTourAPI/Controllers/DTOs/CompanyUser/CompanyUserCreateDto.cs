using System.ComponentModel.DataAnnotations;

namespace TicaTourCompany.Controllers.DTOs.CompanyUser
{
    // Sólo los campos que quieres pedir de CompanyUser
    public record CompanyUserCreateDto(
        [Required, MaxLength(120)] string Name,
        [MaxLength(200)] string Address,
        [MaxLength(300)] string Description,
        [Url] string ImageUrl,
        [Phone] string PhoneNumber);
}
