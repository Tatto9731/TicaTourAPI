namespace TicaTourCompany.Controllers.DTOs.Auth
{
    public record LoginRequest(string UserNameOrEmail, string Password);
    public record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, string UserId, string UserName, string[] Roles);

}
