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
                    p.id_number as IdNumber,
                    p.birth_date::text as BirthDate,
                    p.created_at as CreatedAt,
                    p.updated_at as UpdatedAt,

                    (
                        select count(*)::integer
                        from public.bookings b
                        where b.user_id = p.id
                          and b.status = 'Completed'
                    ) as CompletedExperiences,

                    (
                        select count(*)::integer
                        from public.favorites f
                        where f.user_id = p.id
                    ) as FavoriteExperiences,

                    (
                        select count(*)::integer
                        from public.reviews r
                        where r.user_id = p.id
                    ) as TotalReviews,

                    coalesce(tp.preferences, '[]'::jsonb)::text as PreferencesJson,
                    tp.requires_transport as RequiresTransport,

                    coalesce(
                        jsonb_agg(
                            jsonb_build_object(
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
                left join public.traveler_profiles tp on tp.user_id = p.id
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
                    p.id_number,
                    p.birth_date,
                    p.created_at,
                    p.updated_at,
                    tp.preferences,
                    tp.requires_transport
                limit 1;
            """;

            Console.WriteLine($"[PROFILE:{requestId}] STEP 2 - Creating command. UserId={userId.Value}");
            Console.WriteLine($"[PROFILE:{requestId}] STEP 3 - Executing profile query...");

            var queryWatch = Stopwatch.StartNew();

            var row = await connection.QueryFirstOrDefaultAsync<ProfileMeRow>(
                new CommandDefinition(
                    sql,
                    new { UserId = userId.Value },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

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
            Console.WriteLine($"[PROFILE:{requestId}] STEP 6 - PreferencesJson length={row.PreferencesJson?.Length ?? 0}");

            var companies = ParseCompanies(row.CompaniesJson);
            var preferences = ParseStringArray(row.PreferencesJson);

            var response = new
            {
                data = new
                {
                    email,
                    profile = new
                    {
                        fullName = row.FullName,
                        role = row.Role,
                        phone = row.Phone,
                        avatarUrl = row.AvatarUrl,
                        preferredLanguage = row.PreferredLanguage,
                        preferredCurrency = row.PreferredCurrency,
                        darkMode = row.DarkMode,
                        profileCompletion = row.ProfileCompletion,
                        isIdentityVerified = row.IsIdentityVerified,

                        birthDate = row.BirthDate,
                        hasIdNumber = !string.IsNullOrWhiteSpace(row.IdNumber),
                        idNumberMasked = MaskIdNumber(row.IdNumber),

                        preferences,
                        requiresTransport = row.RequiresTransport,

                        createdAt = row.CreatedAt,
                        updatedAt = row.UpdatedAt
                    },
                    stats = new
                    {
                        completedExperiences = row.CompletedExperiences,
                        favoriteExperiences = row.FavoriteExperiences,
                        totalReviews = row.TotalReviews
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

    private static string[] ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine("[PROFILE] ParseStringArray ERROR:");
            Console.WriteLine(ex.ToString());
            return [];
        }
    }

    private static string? MaskIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            return null;
        }

        var clean = idNumber.Trim();

        if (clean.Length <= 4)
        {
            return "****";
        }

        var lastFour = clean[^4..];

        return $"****{lastFour}";
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

        public string? IdNumber { get; set; }
        public string? BirthDate { get; set; }

        public string PreferencesJson { get; set; } = "[]";
        public bool? RequiresTransport { get; set; }

        public int CompletedExperiences { get; set; }
        public int FavoriteExperiences { get; set; }
        public int TotalReviews { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string CompaniesJson { get; set; } = "[]";
    }
}