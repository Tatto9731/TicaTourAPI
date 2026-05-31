using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Me;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Profile")]
public class MeProfileController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public MeProfileController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpPatch]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMyProfileRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "FullName is required."
                }
            });
        }

        if (request.PreferredLanguage is not ("es" or "en"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_LANGUAGE",
                    message = "PreferredLanguage must be es or en."
                }
            });
        }

        if (request.PreferredCurrency is not ("CRC" or "USD"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_CURRENCY",
                    message = "PreferredCurrency must be CRC or USD."
                }
            });
        }

        const string sql = """
            update public.profiles
            set
                full_name = @FullName,
                phone = @Phone,
                avatar_url = @AvatarUrl,
                preferred_language = @PreferredLanguage,
                preferred_currency = @PreferredCurrency,
                dark_mode = @DarkMode,
                updated_at = now()
            where id = @UserId
            returning
                id,
                full_name,
                role,
                phone,
                avatar_url,
                preferred_language,
                preferred_currency,
                dark_mode,
                profile_completion,
                is_identity_verified,
                created_at,
                updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            request.FullName,
            request.Phone,
            request.AvatarUrl,
            request.PreferredLanguage,
            request.PreferredCurrency,
            request.DarkMode
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "PROFILE_NOT_FOUND",
                    message = "Profile was not found."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Profile updated successfully."
        });
    }

    private Guid? GetUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : null;
    }

    private IActionResult UnauthorizedResponse()
    {
        return Unauthorized(new
        {
            error = new
            {
                code = "UNAUTHORIZED",
                message = "User ID was not found in the token."
            }
        });
    }
}