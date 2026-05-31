using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Experiences/{slug}/Media")]
public class CompanyExperienceMediaController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyExperienceMediaController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetMedia([FromRoute] string slug)
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
                em.id,
                em.type,
                em.url,
                em.sort_order,
                em.alt_text,
                em.created_at
            from public.experience_media em
            inner join public.experiences e on e.id = em.experience_id
            inner join public.companies c on c.id = e.company_id
            where e.slug = @Slug
              and e.is_deleted = false
              and em.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by em.sort_order asc, em.created_at asc;
        """;

        var media = await _connection.QueryAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug
        });

        return Ok(new
        {
            data = media,
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateMedia(
        [FromRoute] string slug,
        [FromBody] CreateExperienceMediaRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateMediaRequest(request.Type, request.Url);

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
            insert into public.experience_media (
                experience_id,
                type,
                url,
                sort_order,
                alt_text,
                created_at
            )
            select
                e.id,
                @Type,
                @Url,
                @SortOrder,
                @AltText,
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
                type,
                url,
                sort_order,
                alt_text,
                created_at;
        """;

        var created = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            request.Type,
            request.Url,
            request.SortOrder,
            request.AltText
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
            message = "Media created successfully."
        });
    }

    [HttpPatch("{mediaId:guid}")]
    public async Task<IActionResult> UpdateMedia(
        [FromRoute] string slug,
        [FromRoute] Guid mediaId,
        [FromBody] UpdateExperienceMediaRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateMediaRequest(request.Type, request.Url);

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
            update public.experience_media em
            set
                type = @Type,
                url = @Url,
                sort_order = @SortOrder,
                alt_text = @AltText
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where em.experience_id = e.id
              and em.id = @MediaId
              and e.slug = @Slug
              and e.is_deleted = false
              and em.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                em.id,
                em.type,
                em.url,
                em.sort_order,
                em.alt_text,
                em.created_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            MediaId = mediaId,
            request.Type,
            request.Url,
            request.SortOrder,
            request.AltText
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "MEDIA_NOT_FOUND",
                    message = "Media was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Media updated successfully."
        });
    }

    [HttpDelete("{mediaId:guid}")]
    public async Task<IActionResult> SoftDeleteMedia(
        [FromRoute] string slug,
        [FromRoute] Guid mediaId)
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
            update public.experience_media em
            set
                is_deleted = true,
                deleted_at = now()
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where em.experience_id = e.id
              and em.id = @MediaId
              and e.slug = @Slug
              and e.is_deleted = false
              and em.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                em.id,
                em.type,
                em.url;
        """;

        var deleted = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            MediaId = mediaId
        });

        if (deleted is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "MEDIA_NOT_FOUND",
                    message = "Media was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                deleted = true,
                media = deleted
            },
            message = "Media deleted logically."
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

    private IActionResult? ValidateMediaRequest(string type, string url)
    {
        if (type is not ("image" or "video"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_MEDIA_TYPE",
                    message = "Type must be 'image' or 'video'."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Url is required."
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