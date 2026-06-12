namespace TicaTourAPI.DTOs.Auth;

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string RedirectTo { get; set; } = string.Empty;
}