using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Experiences/{slug}/Promotions")]
public class CompanyExperiencePromotionsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyExperiencePromotionsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetPromotions([FromRoute] string slug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            select
                p.id,
                p.badge,
                p.title,
                p.description,
                p.discount_percent,
                p.starts_at,
                p.ends_at,
                p.status,
                p.created_at,
                p.updated_at
            from public.promotions p
            inner join public.experiences e on e.id = p.experience_id
            inner join public.companies c on c.id = e.company_id
            where e.slug = @Slug
              and e.is_deleted = false
              and p.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by p.starts_at desc, p.created_at desc;
        """;

        var promotions = await _connection.QueryAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug
        });

        return Ok(new
        {
            data = promotions,
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreatePromotion(
        [FromRoute] string slug,
        [FromBody] CreatePromotionRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidatePromotionRequest(
            request.Badge,
            request.Title,
            request.DiscountPercent,
            request.StartsAt,
            request.EndsAt,
            request.Status);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            insert into public.promotions (
                experience_id,
                badge,
                title,
                description,
                discount_percent,
                starts_at,
                ends_at,
                status,
                created_at,
                updated_at
            )
            select
                e.id,
                @Badge,
                @Title,
                @Description,
                @DiscountPercent,
                @StartsAt,
                @EndsAt,
                @Status,
                now(),
                now()
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where e.slug = @Slug
              and e.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                id,
                badge,
                title,
                description,
                discount_percent,
                starts_at,
                ends_at,
                status,
                created_at,
                updated_at;
        """;

        var created = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            request.Badge,
            request.Title,
            request.Description,
            request.DiscountPercent,
            request.StartsAt,
            request.EndsAt,
            request.Status
        });

        if (created is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "EXPERIENCE_NOT_FOUND",
                    message = "Experience was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = created,
            message = "Promotion created successfully."
        });
    }

    [HttpPatch("{promotionId:guid}")]
    public async Task<IActionResult> UpdatePromotion(
        [FromRoute] string slug,
        [FromRoute] Guid promotionId,
        [FromBody] UpdatePromotionRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidatePromotionRequest(
            request.Badge,
            request.Title,
            request.DiscountPercent,
            request.StartsAt,
            request.EndsAt,
            request.Status);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            update public.promotions p
            set
                badge = @Badge,
                title = @Title,
                description = @Description,
                discount_percent = @DiscountPercent,
                starts_at = @StartsAt,
                ends_at = @EndsAt,
                status = @Status,
                updated_at = now()
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where p.experience_id = e.id
              and p.id = @PromotionId
              and e.slug = @Slug
              and e.is_deleted = false
              and p.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                p.id,
                p.badge,
                p.title,
                p.description,
                p.discount_percent,
                p.starts_at,
                p.ends_at,
                p.status,
                p.created_at,
                p.updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            PromotionId = promotionId,
            request.Badge,
            request.Title,
            request.Description,
            request.DiscountPercent,
            request.StartsAt,
            request.EndsAt,
            request.Status
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "PROMOTION_NOT_FOUND",
                    message = "Promotion was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Promotion updated successfully."
        });
    }

    [HttpDelete("{promotionId:guid}")]
    public async Task<IActionResult> SoftDeletePromotion(
        [FromRoute] string slug,
        [FromRoute] Guid promotionId)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            update public.promotions p
            set
                is_deleted = true,
                deleted_at = now(),
                updated_at = now(),
                status = 'Paused'
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where p.experience_id = e.id
              and p.id = @PromotionId
              and e.slug = @Slug
              and e.is_deleted = false
              and p.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                p.id,
                p.badge,
                p.title,
                p.status;
        """;

        var deleted = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            PromotionId = promotionId
        });

        if (deleted is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "PROMOTION_NOT_FOUND",
                    message = "Promotion was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                deleted = true,
                promotion = deleted
            },
            message = "Promotion deleted logically."
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

    private async Task<bool> UserCanAccessExperienceAsync(Guid userId, string experienceSlug)
    {
        const string sql = """
            select exists (
                select 1
                from public.experiences e
                inner join public.companies c on c.id = e.company_id
                inner join public.company_users cu on cu.company_id = c.id
                inner join public.profiles p on p.id = cu.user_id
                where e.slug = @ExperienceSlug
                  and e.is_deleted = false
                  and cu.user_id = @UserId
                  and p.role = 'company_admin'
            );
        """;

        return await _connection.ExecuteScalarAsync<bool>(sql, new
        {
            UserId = userId,
            ExperienceSlug = experienceSlug
        });
    }

    private IActionResult? ValidatePromotionRequest(
        string badge,
        string title,
        int? discountPercent,
        DateTime startsAt,
        DateTime endsAt,
        string status)
    {
        if (string.IsNullOrWhiteSpace(badge))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Badge is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Title is required."
                }
            });
        }

        if (discountPercent is not null && (discountPercent < 1 || discountPercent > 100))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_DISCOUNT_PERCENT",
                    message = "DiscountPercent must be between 1 and 100."
                }
            });
        }

        if (startsAt == default || endsAt == default)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "StartsAt and EndsAt are required."
                }
            });
        }

        if (startsAt >= endsAt)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_DATE_RANGE",
                    message = "StartsAt must be earlier than EndsAt."
                }
            });
        }

        if (status is not ("Active" or "Scheduled" or "Expired" or "Paused"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_STATUS",
                    message = "Status must be Active, Scheduled, Expired or Paused."
                }
            });
        }

        return null;
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