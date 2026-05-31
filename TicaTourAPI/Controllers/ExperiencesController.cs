using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.Json;
using TicaTourAPI.DTOs;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExperiencesController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public ExperiencesController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetPublicExperiences(
        [FromQuery] string? q,
        [FromQuery] string? province,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
    {
        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

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

                false as IsFavorite
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            inner join public.categories cat on cat.id = e.category_id
            where e.status = 'Published'
              and (@Q is null or e.title ilike '%' || @Q || '%' or c.name ilike '%' || @Q || '%')
              and (@Province is null or e.province ilike @Province)
              and (@Category is null or cat.slug = @Category)
            order by e.is_promoted desc, e.created_at desc
            limit @PerPage offset @Offset;
        """;

        const string countSql = """
            select count(*)
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            inner join public.categories cat on cat.id = e.category_id
            where e.status = 'Published'
              and (@Q is null or e.title ilike '%' || @Q || '%' or c.name ilike '%' || @Q || '%')
              and (@Province is null or e.province ilike @Province)
              and (@Category is null or cat.slug = @Category);
        """;

        var parameters = new
        {
            Q = string.IsNullOrWhiteSpace(q) ? null : q,
            Province = string.IsNullOrWhiteSpace(province) ? null : province,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            PerPage = perPage,
            Offset = offset
        };

        var rows = await _connection.QueryAsync<ExperienceListRow>(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        var data = rows.Select(MapExperience);

        return Ok(new
        {
            data,
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

    private async Task<object> BuildExperienceDetailAsync(ExperienceDetailRow row)
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

        var media = await _connection.QueryAsync(mediaSql, new { row.Slug });
        var pickupStops = await _connection.QueryAsync(pickupSql, new { row.Slug });
        var promotion = await _connection.QueryFirstOrDefaultAsync(promotionSql, new { row.Slug });

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

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetPublicExperienceDetail([FromRoute] string slug)
    {
        const string experienceSql = """
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

            false as IsFavorite
        from public.experiences e
        inner join public.companies c on c.id = e.company_id
        inner join public.categories cat on cat.id = e.category_id
        where e.status = 'Published'
          and e.slug = @Slug
        limit 1;
    """;

        var experience = await _connection.QueryFirstOrDefaultAsync<ExperienceDetailRow>(
            experienceSql,
            new { Slug = slug });

        if (experience is null)
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

        var detail = await BuildExperienceDetailAsync(experience);

        return Ok(new
        {
            data = detail,
            message = "OK"
        });
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


}