using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.Data;
using TicaTourAPI.DTOs.Me;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Favorites")]
public class MeFavoritesController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MeFavoritesController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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
            Console.WriteLine("ME FAVORITES ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_FAVORITES_ERROR",
                    message = ex.Message
                }
            });
        }
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

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string insertSql = """
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

            var favoriteId = await connection.ExecuteScalarAsync<Guid?>(insertSql, new
            {
                UserId = userId.Value,
                request.ExperienceSlug
            });

            if (favoriteId is null)
            {
                const string existsSql = """
                    select exists (
                        select 1
                        from public.experiences
                        where slug = @ExperienceSlug
                          and status = 'Published'
                          and is_deleted = false
                    );
                """;

                var experienceExists = await connection.ExecuteScalarAsync<bool>(existsSql, new
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
        catch (Exception ex)
        {
            Console.WriteLine("SAVE FAVORITE ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "SAVE_FAVORITE_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpDelete("{experienceSlug}")]
    public async Task<IActionResult> RemoveFavorite([FromRoute] string experienceSlug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        if (string.IsNullOrWhiteSpace(experienceSlug))
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

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string sql = """
                delete from public.favorites f
                using public.experiences e
                where f.experience_id = e.id
                  and f.user_id = @UserId
                  and e.slug = @ExperienceSlug
                returning f.id;
            """;

            var removedId = await connection.ExecuteScalarAsync<Guid?>(sql, new
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
        catch (Exception ex)
        {
            Console.WriteLine("REMOVE FAVORITE ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REMOVE_FAVORITE_ERROR",
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

    private sealed class ExperienceListRow
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
}