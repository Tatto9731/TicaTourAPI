namespace TicaTourAPI.DTOs.Auth;

public sealed class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}