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
    private readonly NpgsqlConnection _connection;

    public ProfileController(NpgsqlConnection connection)
    {
        _connection = connection;
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

        try
        {
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

            var profile = await _connection.QueryFirstOrDefaultAsync(profileSql, new
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
                    cu.role as company_role,
                    cu.created_at
                from public.company_users cu
                inner join public.companies c on c.id = cu.company_id
                where cu.user_id = @UserId
                order by cu.created_at desc;
            """;

            var companies = await _connection.QueryAsync(companiesSql, new
            {
                UserId = userId.Value
            });

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
                    companies = companies.Select(c => new
                    {
                        id = (Guid)c.id,
                        name = (string)c.name,
                        slug = (string)c.slug,
                        description = (string?)c.description,
                        province = (string?)c.province,
                        zone = (string?)c.zone,
                        logoUrl = (string?)c.logo_url,
                        coverImageUrl = (string?)c.cover_image_url,
                        phone = (string?)c.phone,
                        whatsapp = (string?)c.whatsapp,
                        email = (string?)c.email,
                        websiteUrl = (string?)c.website_url,
                        isVerified = (bool)c.is_verified,
                        companyRole = (string)c.company_role,
                        createdAt = c.created_at
                    })
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