using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Experiences")]
public class CompanyExperiencesController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyExperiencesController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyCompanyExperiences([FromQuery] string companySlug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            select
                e.public_code,
                e.slug,
                e.title,
                e.province,
                e.zone,
                e.price,
                e.price_currency,
                e.duration_label,
                e.difficulty,
                e.status,
                e.main_image_url,
                e.tags::text as tags_json,
                e.next_slot_label,
                e.is_promoted,
                e.created_at,
                e.updated_at
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where c.slug = @CompanySlug
              and e.is_deleted = false
            order by e.created_at desc;
        """;

        var experiences = await _connection.QueryAsync(sql, new
        {
            CompanySlug = companySlug
        });

        return Ok(new
        {
            data = experiences,
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateExperience([FromBody] CreateCompanyExperienceRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateCreateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, request.CompanySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            insert into public.experiences (
                public_code,
                company_id,
                title,
                slug,
                province,
                zone,
                category_id,
                price,
                price_currency,
                duration_minutes,
                duration_label,
                difficulty,
                status,
                main_image_url,
                tags,
                next_slot_label,
                is_promoted,
                created_at,
                updated_at
            )
            select
                @PublicCode,
                c.id,
                @Title,
                @Slug,
                @Province,
                @Zone,
                cat.id,
                @Price,
                @PriceCurrency,
                @DurationMinutes,
                @DurationLabel,
                @Difficulty,
                'Draft',
                @MainImageUrl,
                cast(@TagsJson as jsonb),
                @NextSlotLabel,
                false,
                now(),
                now()
            from public.companies c
            inner join public.categories cat on cat.slug = @CategorySlug
            where c.slug = @CompanySlug
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning public_code, slug, title, status;
        """;

        try
        {
            var created = await _connection.QueryFirstOrDefaultAsync(sql, new
            {
                UserId = userId.Value,
                request.CompanySlug,
                request.PublicCode,
                request.Title,
                request.Slug,
                request.Province,
                request.Zone,
                request.CategorySlug,
                request.Price,
                request.PriceCurrency,
                request.DurationMinutes,
                request.DurationLabel,
                request.Difficulty,
                request.MainImageUrl,
                TagsJson = JsonSerializer.Serialize(request.Tags),
                request.NextSlotLabel
            });

            if (created is null)
            {
                return BadRequest(new
                {
                    error = new
                    {
                        code = "CREATE_EXPERIENCE_FAILED",
                        message = "Company or category was not found, or user does not have access."
                    }
                });
            }

            return Ok(new
            {
                data = created,
                message = "Experience created successfully."
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new
            {
                error = new
                {
                    code = "DUPLICATE_EXPERIENCE",
                    message = "The public code or slug is already in use."
                }
            });
        }
    }

    [HttpPatch("{slug}")]
    public async Task<IActionResult> UpdateExperience(
        [FromRoute] string slug,
        [FromBody] UpdateCompanyExperienceRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        if (request.Status is not ("Draft" or "Review"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_STATUS",
                    message = "Company admins can only set status to Draft or Review."
                }
            });
        }

        const string sql = """
            update public.experiences e
            set
                title = @Title,
                province = @Province,
                zone = @Zone,
                category_id = cat.id,
                price = @Price,
                price_currency = @PriceCurrency,
                duration_minutes = @DurationMinutes,
                duration_label = @DurationLabel,
                difficulty = @Difficulty,
                status = @Status,
                main_image_url = @MainImageUrl,
                tags = cast(@TagsJson as jsonb),
                next_slot_label = @NextSlotLabel,
                updated_at = now()
            from public.companies c,
                 public.categories cat
            where e.company_id = c.id
              and cat.slug = @CategorySlug
              and e.slug = @Slug
              and e.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning e.public_code, e.slug, e.title, e.status;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            request.Title,
            request.Province,
            request.Zone,
            request.CategorySlug,
            request.Price,
            request.PriceCurrency,
            request.DurationMinutes,
            request.DurationLabel,
            request.Difficulty,
            request.Status,
            request.MainImageUrl,
            TagsJson = JsonSerializer.Serialize(request.Tags),
            request.NextSlotLabel
        });

        if (updated is null)
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
            data = updated,
            message = "Experience updated successfully."
        });
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> SoftDeleteExperience([FromRoute] string slug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        const string sql = """
            update public.experiences e
            set 
                is_deleted = true,
                deleted_at = now(),
                updated_at = now(),
                status = 'Draft'
            from public.companies c
            where e.company_id = c.id
              and e.slug = @Slug
              and e.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning e.public_code, e.slug, e.title;
        """;

        var deleted = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug
        });

        if (deleted is null)
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
            data = new
            {
                deleted = true,
                experience = deleted
            },
            message = "Experience deleted logically."
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

    private IActionResult? ValidateCreateRequest(CreateCompanyExperienceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanySlug) ||
            string.IsNullOrWhiteSpace(request.PublicCode) ||
            string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Slug) ||
            string.IsNullOrWhiteSpace(request.Province) ||
            string.IsNullOrWhiteSpace(request.Zone) ||
            string.IsNullOrWhiteSpace(request.CategorySlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug, PublicCode, Title, Slug, Province, Zone and CategorySlug are required."
                }
            });
        }

        if (request.Price <= 0)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Price must be greater than zero."
                }
            });
        }

        if (request.PriceCurrency is not ("CRC" or "USD"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "PriceCurrency must be CRC or USD."
                }
            });
        }

        if (request.DurationMinutes <= 0)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "DurationMinutes must be greater than zero."
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