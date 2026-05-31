/*using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public ProfileController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        var email = User.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new
            {
                message = "User ID was not found in the token."
            });
        }

        var userGuid = Guid.Parse(userId);

        const string profileSql = """
            select 
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
                updated_at
            from public.profiles
            where id = @UserId;
        """;

        const string companiesSql = """
            select
                c.id,
                c.name,
                c.slug,
                c.description,
                c.province,
                c.zone,
                c.logo_url,
                c.cover_image_url,
                c.phone,
                c.whatsapp,
                c.email,
                c.website_url,
                c.is_verified,
                c.rating_avg,
                c.reviews_count,
                cu.role as company_role,
                c.created_at,
                c.updated_at
            from public.company_users cu
            inner join public.companies c on c.id = cu.company_id
            where cu.user_id = @UserId
            order by c.created_at desc;
        """;

        var profile = await _connection.QueryFirstOrDefaultAsync(profileSql, new
        {
            UserId = userGuid
        });

        if (profile is null)
        {
            return NotFound(new
            {
                message = "Profile was not found for the authenticated user."
            });
        }

        var companies = await _connection.QueryAsync(companiesSql, new
        {
            UserId = userGuid
        });

        return Ok(new
        {
            data = new
            {
                userId,
                email,
                profile,
                companies
            },
            message = "OK"
        });
    }
}*/
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ProfileController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetUserId();

        if (userId is null)
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

        var email = GetEmailFromToken();
        var connectionString = _configuration.GetConnectionString("SupabaseDb");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(500, new
            {
                error = new
                {
                    code = "CONNECTION_STRING_MISSING",
                    message = "SupabaseDb connection string is missing."
                }
            });
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            const string profileSql = """
                select
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
                    updated_at
                from public.profiles
                where id = @UserId
                limit 1;
            """;

            var profile = await connection.QueryFirstOrDefaultAsync(profileSql, new
            {
                UserId = userId.Value
            });

            if (profile is null)
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
                data = new
                {
                    userId = (Guid)profile.id,
                    email,
                    profile = new
                    {
                        id = (Guid)profile.id,
                        fullName = (string?)profile.full_name,
                        role = (string)profile.role,
                        phone = (string?)profile.phone,
                        avatarUrl = (string?)profile.avatar_url,
                        preferredLanguage = (string)profile.preferred_language,
                        preferredCurrency = (string)profile.preferred_currency,
                        darkMode = (bool)profile.dark_mode,
                        profileCompletion = (int)profile.profile_completion,
                        isIdentityVerified = (bool)profile.is_identity_verified,
                        createdAt = profile.created_at,
                        updatedAt = profile.updated_at
                    },
                    companies = Array.Empty<object>()
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("PROFILE ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "PROFILE_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    private Guid? GetUserId()
    {
        var userId =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : null;
    }

    private string? GetEmailFromToken()
    {
        return User.FindFirstValue(ClaimTypes.Email)
               ?? User.FindFirstValue("email");
    }
}