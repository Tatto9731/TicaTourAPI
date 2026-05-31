using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.Data;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Experiences")]
public class MeExperiencesController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MeExperiencesController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetExperiences(
        [FromQuery] string? category,
        [FromQuery] string? province,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string sql = """
                select
                    e.public_code as PublicCode,
                    e.slug as Slug,
                    e.title as Title,
                    e.province as Province,
                    e.zone as Zone,
                    e.price as Price,
                    e.price_currency as PriceCurrency,
                    e.duration_label as Duration,
                    e.rating_avg as Rating,
                    e.reviews_count as Reviews,
                    e.difficulty as Difficulty,
                    e.main_image_url as Image,
                    e.tags::text as TagsJson,
                    e.next_slot_label as NextSlot,
                    e.is_promoted as Promoted,

                    c.name as CompanyName,
                    c.slug as CompanySlug,
                    c.is_verified as CompanyVerified,

                    cat.name as CategoryName,
                    cat.slug as CategorySlug,

                    case 
                        when f.id is null then false 
                        else true 
                    end as IsFavorite
                from public.experiences e
                inner join public.companies c on c.id = e.company_id
                inner join public.categories cat on cat.id = e.category_id
                left join public.favorites f 
                    on f.experience_id = e.id
                   and f.user_id = @UserId
                where e.status = 'Published'
                  and e.is_deleted = false
                  and (@Category is null or cat.slug = @Category)
                  and (@Province is null or e.province = @Province)
                  and (
                        @Q is null
                        or e.title ilike '%' || @Q || '%'
                        or e.zone ilike '%' || @Q || '%'
                        or e.province ilike '%' || @Q || '%'
                      )
                order by e.is_promoted desc, e.created_at desc
                limit @PerPage offset @Offset;
            """;

            const string countSql = """
                select count(*)
                from public.experiences e
                inner join public.categories cat on cat.id = e.category_id
                where e.status = 'Published'
                  and e.is_deleted = false
                  and (@Category is null or cat.slug = @Category)
                  and (@Province is null or e.province = @Province)
                  and (
                        @Q is null
                        or e.title ilike '%' || @Q || '%'
                        or e.zone ilike '%' || @Q || '%'
                        or e.province ilike '%' || @Q || '%'
                      );
            """;

            var parameters = new
            {
                UserId = userId.Value,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                Province = string.IsNullOrWhiteSpace(province) ? null : province,
                Q = string.IsNullOrWhiteSpace(q) ? null : q,
                PerPage = perPage,
                Offset = offset
            };

            var rows = await connection.QueryAsync<ExperienceListRow>(sql, parameters);
            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            return Ok(new
            {
                data = rows.Select(MapExperience),
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
            Console.WriteLine("ME EXPERIENCES ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_EXPERIENCES_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetExperienceDetail([FromRoute] string slug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string sql = """
                select
                    e.public_code as PublicCode,
                    e.slug as Slug,
                    e.title as Title,
                    e.province as Province,
                    e.zone as Zone,
                    e.price as Price,
                    e.price_currency as PriceCurrency,
                    e.duration_label as Duration,
                    e.rating_avg as Rating,
                    e.reviews_count as Reviews,
                    e.difficulty as Difficulty,
                    e.status as Status,
                    e.main_image_url as Image,
                    e.video_url as Video,
                    e.tags::text as TagsJson,
                    e.next_slot_label as NextSlot,
                    e.is_promoted as Promoted,

                    c.name as CompanyName,
                    c.slug as CompanySlug,
                    c.is_verified as CompanyVerified,

                    cat.name as CategoryName,
                    cat.slug as CategorySlug,

                    case 
                        when f.id is null then false 
                        else true 
                    end as IsFavorite
                from public.experiences e
                inner join public.companies c on c.id = e.company_id
                inner join public.categories cat on cat.id = e.category_id
                left join public.favorites f 
                    on f.experience_id = e.id
                   and f.user_id = @UserId
                where e.slug = @Slug
                  and e.status = 'Published'
                  and e.is_deleted = false
                limit 1;
            """;

            var row = await connection.QueryFirstOrDefaultAsync<ExperienceDetailRow>(sql, new
            {
                UserId = userId.Value,
                Slug = slug
            });

            if (row is null)
            {
                return NotFound(new
                {
                    error = new
                    {
                        code = "EXPERIENCE_NOT_FOUND",
                        message = "Experience was not found."
                    }
                });
            }

            var detail = await BuildExperienceDetailAsync(connection, row);

            return Ok(new
            {
                data = detail,
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ME EXPERIENCE DETAIL ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_EXPERIENCE_DETAIL_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    private static object MapExperience(ExperienceListRow row)
    {
        return new
        {
            publicCode = row.PublicCode,
            slug = row.Slug,
            title = row.Title,
            province = row.Province,
            zone = row.Zone,
            price = row.Price,
            priceCurrency = row.PriceCurrency,
            duration = row.Duration,
            rating = row.Rating,
            reviews = row.Reviews,
            difficulty = row.Difficulty,
            image = row.Image,
            tags = ParseTags(row.TagsJson),
            nextSlot = row.NextSlot,
            promoted = row.Promoted,
            isFavorite = row.IsFavorite,
            company = new
            {
                name = row.CompanyName,
                slug = row.CompanySlug,
                verified = row.CompanyVerified
            },
            category = new
            {
                name = row.CategoryName,
                slug = row.CategorySlug
            }
        };
    }

    private static async Task<object> BuildExperienceDetailAsync(
        System.Data.IDbConnection connection,
        ExperienceDetailRow row)
    {
        const string mediaSql = """
            select
                type,
                url,
                sort_order,
                alt_text
            from public.experience_media
            where experience_id = (
                select id
                from public.experiences
                where slug = @Slug
                  and is_deleted = false
                limit 1
            )
              and is_deleted = false
            order by sort_order asc;
        """;

        const string pickupSql = """
            select
                place,
                time_label,
                latitude,
                longitude,
                sort_order
            from public.pickup_stops
            where experience_id = (
                select id
                from public.experiences
                where slug = @Slug
                  and is_deleted = false
                limit 1
            )
              and is_deleted = false
            order by sort_order asc;
        """;

        const string promotionSql = """
            select
                badge,
                title,
                description,
                discount_percent
            from public.promotions
            where experience_id = (
                select id
                from public.experiences
                where slug = @Slug
                  and is_deleted = false
                limit 1
            )
              and is_deleted = false
              and status = 'Active'
              and starts_at <= now()
              and ends_at >= now()
            order by starts_at desc
            limit 1;
        """;

        var media = await connection.QueryAsync(mediaSql, new { row.Slug });
        var pickupStops = await connection.QueryAsync(pickupSql, new { row.Slug });
        var promotion = await connection.QueryFirstOrDefaultAsync(promotionSql, new { row.Slug });

        var images = media
            .Where(m => ((string)m.type) == "image")
            .Select(m => new
            {
                url = (string)m.url,
                altText = (string?)m.alt_text
            });

        return new
        {
            publicCode = row.PublicCode,
            slug = row.Slug,
            title = row.Title,
            province = row.Province,
            zone = row.Zone,
            category = new
            {
                name = row.CategoryName,
                slug = row.CategorySlug
            },
            company = new
            {
                name = row.CompanyName,
                slug = row.CompanySlug,
                verified = row.CompanyVerified
            },
            price = row.Price,
            priceCurrency = row.PriceCurrency,
            duration = row.Duration,
            rating = row.Rating,
            reviews = row.Reviews,
            difficulty = row.Difficulty,
            status = row.Status,
            image = row.Image,
            images,
            video = row.Video,
            tags = ParseTags(row.TagsJson),
            nextSlot = row.NextSlot,
            promoted = row.Promoted,
            transport = new
            {
                pickupStops = pickupStops.Select(p => new
                {
                    place = (string)p.place,
                    time = (string)p.time_label,
                    latitude = (decimal?)p.latitude,
                    longitude = (decimal?)p.longitude
                })
            },
            promotion = promotion is null
                ? null
                : new
                {
                    badge = (string)promotion.badge,
                    title = (string)promotion.title,
                    description = (string?)promotion.description,
                    discountPercent = (int?)promotion.discount_percent
                },
            isFavorite = row.IsFavorite
        };
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

    private static string[] ParseTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(tagsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private class ExperienceListRow
    {
        public string PublicCode { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string PriceCurrency { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public decimal Rating { get; set; }
        public int Reviews { get; set; }
        public string? Difficulty { get; set; }
        public string? Image { get; set; }
        public string? TagsJson { get; set; }
        public string? NextSlot { get; set; }
        public bool Promoted { get; set; }
        public bool IsFavorite { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string CompanySlug { get; set; } = string.Empty;
        public bool CompanyVerified { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategorySlug { get; set; } = string.Empty;
    }

    private sealed class ExperienceDetailRow : ExperienceListRow
    {
        public string Status { get; set; } = string.Empty;
        public string? Video { get; set; }
    }
}