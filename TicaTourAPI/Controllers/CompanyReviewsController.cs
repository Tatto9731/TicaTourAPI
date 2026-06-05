using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.Data;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Reviews")]
public class CompanyReviewsController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CompanyReviewsController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanyReviews(
        [FromQuery] string companySlug,
        [FromQuery] string status = "all",
        [FromQuery] string? experienceSlug = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        if (string.IsNullOrWhiteSpace(companySlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug is required."
                }
            });
        }

        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        var statusFilter = status switch
        {
            "Published" => "and r.status = 'Published'",
            "PendingModeration" => "and r.status = 'PendingModeration'",
            "Rejected" => "and r.status = 'Rejected'",
            "all" => "",
            _ => ""
        };

        var experienceFilter = string.IsNullOrWhiteSpace(experienceSlug)
            ? ""
            : "and e.slug = @ExperienceSlug";

        var sql = $"""
            select
                r.rating,
                r.comment,
                r.status,
                r.created_at,

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title,
                e.main_image_url as experience_image,

                b.booking_code,

                p.full_name as traveler_name,
                p.avatar_url as traveler_avatar_url
            from public.reviews r
            inner join public.experiences e on e.id = r.experience_id
            inner join public.companies c on c.id = e.company_id
            left join public.bookings b on b.id = r.booking_id
            left join public.profiles p on p.id = r.user_id
            where c.slug = @CompanySlug
              {statusFilter}
              {experienceFilter}
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by r.created_at desc
            limit @PerPage offset @Offset;
        """;

        var countSql = $"""
            select count(*)
            from public.reviews r
            inner join public.experiences e on e.id = r.experience_id
            inner join public.companies c on c.id = e.company_id
            where c.slug = @CompanySlug
              {statusFilter}
              {experienceFilter}
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              );
        """;

        var parameters = new
        {
            UserId = userId.Value,
            CompanySlug = companySlug,
            ExperienceSlug = string.IsNullOrWhiteSpace(experienceSlug) ? null : experienceSlug,
            PerPage = perPage,
            Offset = offset
        };

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var canAccess = await UserCanAccessCompanyAsync(
                connection,
                userId.Value,
                companySlug,
                cancellationToken);

            if (!canAccess)
            {
                return Forbid();
            }

            var reviews = await connection.QueryAsync(
                new CommandDefinition(
                    sql,
                    parameters,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            var total = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    countSql,
                    parameters,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            return Ok(new
            {
                data = reviews.Select(r => new
                {
                    rating = (int)r.rating,
                    comment = (string)r.comment,
                    status = (string)r.status,
                    createdAt = r.created_at,
                    bookingCode = (string?)r.booking_code,
                    traveler = new
                    {
                        name = (string?)r.traveler_name,
                        avatarUrl = (string?)r.traveler_avatar_url
                    },
                    experience = new
                    {
                        publicCode = (string)r.experience_public_code,
                        slug = (string)r.experience_slug,
                        title = (string)r.experience_title,
                        image = (string?)r.experience_image
                    }
                }),
                meta = new
                {
                    page,
                    perPage,
                    total,
                    totalPages = (int)Math.Ceiling(total / (double)perPage)
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("GET COMPANY REVIEWS ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "GET_COMPANY_REVIEWS_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetCompanyReviewsSummary(
        [FromQuery] string companySlug,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        if (string.IsNullOrWhiteSpace(companySlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug is required."
                }
            });
        }

        const string sql = """
            select
                coalesce(round(avg(r.rating)::numeric, 2), 0) as rating_avg,
                count(*)::integer as reviews_count,
                count(*) filter (where r.rating = 5)::integer as five_stars,
                count(*) filter (where r.rating = 4)::integer as four_stars,
                count(*) filter (where r.rating = 3)::integer as three_stars,
                count(*) filter (where r.rating = 2)::integer as two_stars,
                count(*) filter (where r.rating = 1)::integer as one_star
            from public.reviews r
            inner join public.experiences e on e.id = r.experience_id
            inner join public.companies c on c.id = e.company_id
            where c.slug = @CompanySlug
              and r.status = 'Published'
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              );
        """;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var canAccess = await UserCanAccessCompanyAsync(
                connection,
                userId.Value,
                companySlug,
                cancellationToken);

            if (!canAccess)
            {
                return Forbid();
            }

            var summary = await connection.QueryFirstOrDefaultAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        UserId = userId.Value,
                        CompanySlug = companySlug
                    },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            return Ok(new
            {
                data = new
                {
                    ratingAverage = (decimal)(summary?.rating_avg ?? 0),
                    reviewsCount = (int)(summary?.reviews_count ?? 0),
                    distribution = new
                    {
                        fiveStars = (int)(summary?.five_stars ?? 0),
                        fourStars = (int)(summary?.four_stars ?? 0),
                        threeStars = (int)(summary?.three_stars ?? 0),
                        twoStars = (int)(summary?.two_stars ?? 0),
                        oneStar = (int)(summary?.one_star ?? 0)
                    }
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("GET COMPANY REVIEWS SUMMARY ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "GET_COMPANY_REVIEWS_SUMMARY_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    private Guid? GetUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<bool> UserCanAccessCompanyAsync(
        NpgsqlConnection connection,
        Guid userId,
        string companySlug,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from public.company_users cu
                inner join public.companies c on c.id = cu.company_id
                inner join public.profiles p on p.id = cu.user_id
                where cu.user_id = @UserId
                  and c.slug = @CompanySlug
                  and p.role = 'company_admin'
            );
        """;

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    CompanySlug = companySlug
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
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