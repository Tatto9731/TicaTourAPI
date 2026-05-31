using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.DTOs;
using TicaTourAPI.DTOs.Me;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Favorites")]
public class MeFavoritesController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public MeFavoritesController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyFavorites(
        [FromQuery] string? category,
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

                true as IsFavorite
            from public.favorites f
            inner join public.experiences e on e.id = f.experience_id
            inner join public.companies c on c.id = e.company_id
            inner join public.categories cat on cat.id = e.category_id
            where f.user_id = @UserId
              and e.status = 'Published'
              and e.is_deleted = false
              and (@Category is null or cat.slug = @Category)
            order by f.created_at desc
            limit @PerPage offset @Offset;
        """;

        const string countSql = """
            select count(*)
            from public.favorites f
            inner join public.experiences e on e.id = f.experience_id
            inner join public.categories cat on cat.id = e.category_id
            where f.user_id = @UserId
              and e.status = 'Published'
              and e.is_deleted = false
              and (@Category is null or cat.slug = @Category);
        """;

        var parameters = new
        {
            UserId = userId.Value,
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

    [HttpPost]
    public async Task<IActionResult> SaveFavorite([FromBody] SaveFavoriteRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        if (string.IsNullOrWhiteSpace(request.ExperienceSlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "ExperienceSlug is required."
                }
            });
        }

        const string sql = """
            insert into public.favorites (
                user_id,
                experience_id,
                created_at
            )
            select
                @UserId,
                e.id,
                now()
            from public.experiences e
            where e.slug = @ExperienceSlug
              and e.status = 'Published'
              and e.is_deleted = false
            on conflict (user_id, experience_id) do nothing
            returning id;
        """;

        var favoriteId = await _connection.ExecuteScalarAsync<Guid?>(sql, new
        {
            UserId = userId.Value,
            request.ExperienceSlug
        });

        if (favoriteId is null)
        {
            var existsSql = """
                select exists (
                    select 1
                    from public.experiences
                    where slug = @ExperienceSlug
                      and status = 'Published'
                      and is_deleted = false
                );
            """;

            var experienceExists = await _connection.ExecuteScalarAsync<bool>(existsSql, new
            {
                request.ExperienceSlug
            });

            if (!experienceExists)
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
        }

        return Ok(new
        {
            data = new
            {
                saved = true,
                experienceSlug = request.ExperienceSlug
            },
            message = "Favorite saved successfully."
        });
    }

    [HttpDelete("{experienceSlug}")]
    public async Task<IActionResult> RemoveFavorite([FromRoute] string experienceSlug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        const string sql = """
            delete from public.favorites f
            using public.experiences e
            where f.experience_id = e.id
              and f.user_id = @UserId
              and e.slug = @ExperienceSlug
            returning f.id;
        """;

        var removedId = await _connection.ExecuteScalarAsync<Guid?>(sql, new
        {
            UserId = userId.Value,
            ExperienceSlug = experienceSlug
        });

        return Ok(new
        {
            data = new
            {
                removed = removedId is not null,
                experienceSlug
            },
            message = "OK"
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