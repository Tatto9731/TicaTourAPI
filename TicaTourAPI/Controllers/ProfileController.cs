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
using System.Diagnostics;
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
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var totalWatch = Stopwatch.StartNew();

        Console.WriteLine($"[PROFILE:{requestId}] START /api/Profile/me");

        var userId = GetUserId();

        Console.WriteLine($"[PROFILE:{requestId}] Token userId raw parsed: {(userId is null ? "NULL" : userId.Value.ToString())}");

        if (userId is null)
        {
            Console.WriteLine($"[PROFILE:{requestId}] END Unauthorized - User ID not found in token. ElapsedMs={totalWatch.ElapsedMilliseconds}");

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

        Console.WriteLine($"[PROFILE:{requestId}] Token email: {email ?? "NULL"}");

        try
        {
            Console.WriteLine($"[PROFILE:{requestId}] STEP 1 - Opening DB connection...");

            var connectionWatch = Stopwatch.StartNew();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            connectionWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] STEP 1 OK - DB connection opened. ElapsedMs={connectionWatch.ElapsedMilliseconds}");
            Console.WriteLine($"[PROFILE:{requestId}] DB State after open: {connection.State}");

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

            Console.WriteLine($"[PROFILE:{requestId}] STEP 2 - Creating command. UserId={userId.Value}");

            var command = new CommandDefinition(
                sql,
                new { UserId = userId.Value },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            Console.WriteLine($"[PROFILE:{requestId}] STEP 3 - Executing profile query...");

            var queryWatch = Stopwatch.StartNew();

            var row = await connection.QueryFirstOrDefaultAsync<ProfileMeRow>(command);

            queryWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] STEP 3 OK - Query finished. ElapsedMs={queryWatch.ElapsedMilliseconds}");

            if (row is null)
            {
                Console.WriteLine($"[PROFILE:{requestId}] END NotFound - Profile not found. ElapsedMs={totalWatch.ElapsedMilliseconds}");

                return NotFound(new
                {
                    error = new
                    {
                        code = "PROFILE_NOT_FOUND",
                        message = "Profile was not found."
                    }
                });
            }

            Console.WriteLine($"[PROFILE:{requestId}] STEP 4 - Row found. Role={row.Role}, FullName={row.FullName ?? "NULL"}");
            Console.WriteLine($"[PROFILE:{requestId}] STEP 5 - CompaniesJson length={row.CompaniesJson?.Length ?? 0}");

            Console.WriteLine($"[PROFILE:{requestId}] STEP 6 - Parsing companies JSON...");

            var parseWatch = Stopwatch.StartNew();

            var companies = ParseCompanies(row.CompaniesJson);

            parseWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] STEP 6 OK - Companies parsed. Count={companies.Length}, ElapsedMs={parseWatch.ElapsedMilliseconds}");

            Console.WriteLine($"[PROFILE:{requestId}] STEP 7 - Building response...");

            var response = new
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
            };

            totalWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] END OK. TotalElapsedMs={totalWatch.ElapsedMilliseconds}");

            return Ok(response);
        }
        catch (OperationCanceledException ex)
        {
            totalWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] CANCELLED. TotalElapsedMs={totalWatch.ElapsedMilliseconds}");
            Console.WriteLine(ex.ToString());

            return StatusCode(499, new
            {
                error = new
                {
                    code = "REQUEST_CANCELLED",
                    message = "The request was cancelled."
                }
            });
        }
        catch (NpgsqlException ex)
        {
            totalWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] NPGSQL ERROR. TotalElapsedMs={totalWatch.ElapsedMilliseconds}");
            Console.WriteLine($"[PROFILE:{requestId}] Npgsql Message: {ex.Message}");
            Console.WriteLine($"[PROFILE:{requestId}] Inner Message: {ex.InnerException?.Message ?? "NULL"}");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "PROFILE_DATABASE_ERROR",
                    message = ex.Message,
                    innerMessage = ex.InnerException?.Message
                }
            });
        }
        catch (Exception ex)
        {
            totalWatch.Stop();

            Console.WriteLine($"[PROFILE:{requestId}] GENERAL ERROR. TotalElapsedMs={totalWatch.ElapsedMilliseconds}");
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
        catch (Exception ex)
        {
            Console.WriteLine("[PROFILE] ParseCompanies ERROR:");
            Console.WriteLine(ex.ToString());
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