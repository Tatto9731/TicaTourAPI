using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.DTOs;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Experiences")]
public class MeExperiencesController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public MeExperiencesController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetPersonalizedExperiences(
        [FromQuery] string? q,
        [FromQuery] string? province,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
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

                case when f.id is null then false else true end as IsFavorite
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            inner join public.categories cat on cat.id = e.category_id
            left join public.favorites f 
                on f.experience_id = e.id
               and f.user_id = @UserId
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
            UserId = Guid.Parse(userId),
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