/*using Dapper;
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

            var companies = await connection.QueryAsync(companiesSql, new
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
}*/
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.Data;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProfileController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
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
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = """
                select
                    p.id as UserId,
                    p.full_name as FullName,
                    p.role as Role,
                    p.phone as Phone,
                    p.avatar_url as AvatarUrl,
                    p.preferred_language as PreferredLanguage,
                    p.preferred_currency as PreferredCurrency,
                    p.dark_mode as DarkMode,
                    p.profile_completion as ProfileCompletion,
                    p.is_identity_verified as IsIdentityVerified,
                    p.created_at as CreatedAt,
                    p.updated_at as UpdatedAt,

                    coalesce(
                        jsonb_agg(
                            jsonb_build_object(
                                'id', c.id,
                                'name', c.name,
                                'slug', c.slug,
                                'description', c.description,
                                'province', c.province,
                                'zone', c.zone,
                                'logoUrl', c.logo_url,
                                'coverImageUrl', c.cover_image_url,
                                'phone', c.phone,
                                'whatsapp', c.whatsapp,
                                'email', c.email,
                                'websiteUrl', c.website_url,
                                'isVerified', c.is_verified,
                                'companyRole', cu.role,
                                'createdAt', cu.created_at
                            )
                        ) filter (where c.id is not null),
                        '[]'::jsonb
                    )::text as CompaniesJson

                from public.profiles p
                left join public.company_users cu on cu.user_id = p.id
                left join public.companies c on c.id = cu.company_id
                where p.id = @UserId
                group by
                    p.id,
                    p.full_name,
                    p.role,
                    p.phone,
                    p.avatar_url,
                    p.preferred_language,
                    p.preferred_currency,
                    p.dark_mode,
                    p.profile_completion,
                    p.is_identity_verified,
                    p.created_at,
                    p.updated_at
                limit 1;
            """;

            var command = new CommandDefinition(
                sql,
                new { UserId = userId.Value },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            var row = await connection.QueryFirstOrDefaultAsync<ProfileMeRow>(command);

            if (row is null)
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

            var companies = ParseCompanies(row.CompaniesJson);

            return Ok(new
            {
                data = new
                {
                    userId = row.UserId,
                    email,
                    profile = new
                    {
                        id = row.UserId,
                        fullName = row.FullName,
                        role = row.Role,
                        phone = row.Phone,
                        avatarUrl = row.AvatarUrl,
                        preferredLanguage = row.PreferredLanguage,
                        preferredCurrency = row.PreferredCurrency,
                        darkMode = row.DarkMode,
                        profileCompletion = row.ProfileCompletion,
                        isIdentityVerified = row.IsIdentityVerified,
                        createdAt = row.CreatedAt,
                        updatedAt = row.UpdatedAt
                    },
                    companies
                },
                message = "OK"
            });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new
            {
                error = new
                {
                    code = "REQUEST_CANCELLED",
                    message = "The request was cancelled."
                }
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

    private static object[] ParseCompanies(string? companiesJson)
    {
        if (string.IsNullOrWhiteSpace(companiesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<object[]>(companiesJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class ProfileMeRow
    {
        public Guid UserId { get; set; }
        public string? FullName { get; set; }
        public string Role { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public string PreferredLanguage { get; set; } = "es";
        public string PreferredCurrency { get; set; } = "CRC";
        public bool DarkMode { get; set; }
        public int ProfileCompletion { get; set; }
        public bool IsIdentityVerified { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CompaniesJson { get; set; } = "[]";
    }
}