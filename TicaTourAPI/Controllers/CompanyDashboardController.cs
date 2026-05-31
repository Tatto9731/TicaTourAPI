using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Dashboard")]
public class CompanyDashboardController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyDashboardController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string companySlug,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo)
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

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        var from = dateFrom?.Date ?? DateTime.UtcNow.Date.AddDays(-30);
        var to = dateTo?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

        var kpis = await GetKpisAsync(companySlug, from, to);
        var topExperiences = await GetTopExperiencesAsync(companySlug, from, to);
        var recentBookings = await GetRecentBookingsAsync(companySlug, from, to);
        var recentLeads = await GetRecentLeadsAsync(companySlug, from, to);

        return Ok(new
        {
            data = new
            {
                dateFrom = from,
                dateTo = to,
                kpis,
                topExperiences,
                recentBookings,
                recentLeads,
                campaignPerformance = Array.Empty<object>()
            },
            message = "OK"
        });
    }

    private async Task<object> GetKpisAsync(string companySlug, DateTime dateFrom, DateTime dateTo)
    {
        const string sql = """
            select
                coalesce(sum(
                    case 
                        when b.status in ('Confirmed', 'Completed') 
                        then b.total_amount 
                        else 0 
                    end
                ), 0) as revenue,

                count(b.id)::integer as bookings,

                coalesce((
                    select count(l.id)::integer
                    from public.leads l
                    inner join public.companies c2 on c2.id = l.company_id
                    where c2.slug = @CompanySlug
                      and l.created_at between @DateFrom and @DateTo
                ), 0) as leads,

                coalesce(sum(e.views_count), 0)::integer as views
            from public.companies c
            left join public.bookings b 
                on b.company_id = c.id
               and b.created_at between @DateFrom and @DateTo
            left join public.experiences e 
                on e.company_id = c.id
               and e.is_deleted = false
            where c.slug = @CompanySlug
            group by c.id;
        """;

        var result = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            CompanySlug = companySlug,
            DateFrom = dateFrom,
            DateTo = dateTo
        });

        if (result is null)
        {
            return new
            {
                revenue = 0,
                bookings = 0,
                leads = 0,
                conversionRate = 0m,
                views = 0
            };
        }

        decimal revenue = (decimal)result.revenue;
        int bookings = (int)result.bookings;
        int leads = (int)result.leads;
        int views = (int)result.views;

        var conversionRate = leads == 0
            ? 0m
            : Math.Round(bookings / (decimal)leads, 2);

        return new
        {
            revenue,
            bookings,
            leads,
            conversionRate,
            views
        };
    }

    private async Task<IEnumerable<object>> GetTopExperiencesAsync(
        string companySlug,
        DateTime dateFrom,
        DateTime dateTo)
    {
        const string sql = """
            select
                e.public_code,
                e.slug,
                e.title,
                e.main_image_url,
                e.views_count,
                e.favorites_count,
                e.rating_avg,
                e.reviews_count,
                count(b.id)::integer as bookings,
                coalesce(sum(
                    case 
                        when b.status in ('Confirmed', 'Completed') 
                        then b.total_amount 
                        else 0 
                    end
                ), 0) as revenue
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            left join public.bookings b 
                on b.experience_id = e.id
               and b.created_at between @DateFrom and @DateTo
            where c.slug = @CompanySlug
              and e.is_deleted = false
            group by
                e.id,
                e.public_code,
                e.slug,
                e.title,
                e.main_image_url,
                e.views_count,
                e.favorites_count,
                e.rating_avg,
                e.reviews_count
            order by bookings desc, revenue desc, e.views_count desc
            limit 5;
        """;

        var rows = await _connection.QueryAsync(sql, new
        {
            CompanySlug = companySlug,
            DateFrom = dateFrom,
            DateTo = dateTo
        });

        return rows.Select(e => new
        {
            publicCode = (string)e.public_code,
            slug = (string)e.slug,
            title = (string)e.title,
            image = (string?)e.main_image_url,
            views = (int)e.views_count,
            favorites = (int)e.favorites_count,
            rating = (decimal)e.rating_avg,
            reviews = (int)e.reviews_count,
            bookings = (int)e.bookings,
            revenue = (decimal)e.revenue
        });
    }

    private async Task<IEnumerable<object>> GetRecentBookingsAsync(
        string companySlug,
        DateTime dateFrom,
        DateTime dateTo)
    {
        const string sql = """
            select
                b.booking_code,
                b.status,
                b.total_amount,
                b.currency,
                b.created_at,
                e.slug as experience_slug,
                e.title as experience_title,
                p.full_name as traveler_name
            from public.bookings b
            inner join public.companies c on c.id = b.company_id
            inner join public.experiences e on e.id = b.experience_id
            left join public.profiles p on p.id = b.user_id
            where c.slug = @CompanySlug
              and b.created_at between @DateFrom and @DateTo
            order by b.created_at desc
            limit 5;
        """;

        var rows = await _connection.QueryAsync(sql, new
        {
            CompanySlug = companySlug,
            DateFrom = dateFrom,
            DateTo = dateTo
        });

        return rows.Select(b => new
        {
            bookingCode = (string)b.booking_code,
            status = (string)b.status,
            totalAmount = (decimal)b.total_amount,
            currency = (string)b.currency,
            createdAt = b.created_at,
            travelerName = (string?)b.traveler_name,
            experience = new
            {
                slug = (string)b.experience_slug,
                title = (string)b.experience_title
            }
        });
    }

    private async Task<IEnumerable<object>> GetRecentLeadsAsync(
        string companySlug,
        DateTime dateFrom,
        DateTime dateTo)
    {
        const string sql = """
            select
                l.id,
                l.traveler_name,
                l.traveler_email,
                l.traveler_phone,
                l.channel,
                l.status,
                l.estimated_value,
                l.currency,
                l.created_at,
                e.slug as experience_slug,
                e.title as experience_title
            from public.leads l
            inner join public.companies c on c.id = l.company_id
            left join public.experiences e on e.id = l.experience_id
            where c.slug = @CompanySlug
              and l.created_at between @DateFrom and @DateTo
            order by l.created_at desc
            limit 5;
        """;

        var rows = await _connection.QueryAsync(sql, new
        {
            CompanySlug = companySlug,
            DateFrom = dateFrom,
            DateTo = dateTo
        });

        return rows.Select(l => new
        {
            id = (Guid)l.id,
            travelerName = (string)l.traveler_name,
            travelerEmail = (string?)l.traveler_email,
            travelerPhone = (string?)l.traveler_phone,
            channel = (string)l.channel,
            status = (string)l.status,
            estimatedValue = (decimal)l.estimated_value,
            currency = (string)l.currency,
            createdAt = l.created_at,
            experience = l.experience_slug is null
                ? null
                : new
                {
                    slug = (string?)l.experience_slug,
                    title = (string?)l.experience_title
                }
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

    private async Task<bool> UserCanAccessCompanyAsync(Guid userId, string companySlug)
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

        return await _connection.ExecuteScalarAsync<bool>(sql, new
        {
            UserId = userId,
            CompanySlug = companySlug
        });
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