using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Categories;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Admin/Categories")]
public class AdminCategoriesController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public AdminCategoriesController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminCategories(
        [FromQuery] bool includeInactive = true,
        [FromQuery] bool includeDeleted = false)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var isPlatformAdmin = await IsPlatformAdminAsync(userId.Value);

        if (!isPlatformAdmin)
        {
            return Forbid();
        }

        const string sql = """
            select
                name,
                slug,
                image_url,
                is_active,
                sort_order,
                is_deleted,
                created_at,
                updated_at,
                deleted_at
            from public.categories
            where (@IncludeInactive = true or is_active = true)
              and (@IncludeDeleted = true or is_deleted = false)
            order by sort_order asc, name asc;
        """;

        var categories = await _connection.QueryAsync(sql, new
        {
            IncludeInactive = includeInactive,
            IncludeDeleted = includeDeleted
        });

        return Ok(new
        {
            data = categories.Select(c => new
            {
                name = (string)c.name,
                slug = (string)c.slug,
                imageUrl = (string?)c.image_url,
                isActive = (bool)c.is_active,
                sortOrder = (int)c.sort_order,
                isDeleted = (bool)c.is_deleted,
                createdAt = c.created_at,
                updatedAt = c.updated_at,
                deletedAt = c.deleted_at
            }),
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var isPlatformAdmin = await IsPlatformAdminAsync(userId.Value);

        if (!isPlatformAdmin)
        {
            return Forbid();
        }

        var validation = ValidateCreateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        const string sql = """
            insert into public.categories (
                name,
                slug,
                image_url,
                is_active,
                sort_order,
                created_at,
                updated_at,
                is_deleted
            )
            values (
                @Name,
                @Slug,
                @ImageUrl,
                @IsActive,
                @SortOrder,
                now(),
                now(),
                false
            )
            returning
                name,
                slug,
                image_url,
                is_active,
                sort_order,
                created_at,
                updated_at;
        """;

        try
        {
            var created = await _connection.QueryFirstOrDefaultAsync(sql, new
            {
                request.Name,
                request.Slug,
                request.ImageUrl,
                request.IsActive,
                request.SortOrder
            });

            return Ok(new
            {
                data = created,
                message = "Category created successfully."
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new
            {
                error = new
                {
                    code = "CATEGORY_SLUG_ALREADY_EXISTS",
                    message = "The category slug is already in use."
                }
            });
        }
    }

    [HttpPatch("{slug}")]
    public async Task<IActionResult> UpdateCategory(
        [FromRoute] string slug,
        [FromBody] UpdateCategoryRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var isPlatformAdmin = await IsPlatformAdminAsync(userId.Value);

        if (!isPlatformAdmin)
        {
            return Forbid();
        }

        var validation = ValidateUpdateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        const string sql = """
            update public.categories
            set
                name = @Name,
                image_url = @ImageUrl,
                is_active = @IsActive,
                sort_order = @SortOrder,
                updated_at = now()
            where slug = @Slug
              and is_deleted = false
            returning
                name,
                slug,
                image_url,
                is_active,
                sort_order,
                created_at,
                updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            Slug = slug,
            request.Name,
            request.ImageUrl,
            request.IsActive,
            request.SortOrder
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "CATEGORY_NOT_FOUND",
                    message = "Category was not found."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Category updated successfully."
        });
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> SoftDeleteCategory([FromRoute] string slug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var isPlatformAdmin = await IsPlatformAdminAsync(userId.Value);

        if (!isPlatformAdmin)
        {
            return Forbid();
        }

        const string categoryInUseSql = """
            select exists (
                select 1
                from public.experiences e
                inner join public.categories c on c.id = e.category_id
                where c.slug = @Slug
                  and e.is_deleted = false
            );
        """;

        var categoryInUse = await _connection.ExecuteScalarAsync<bool>(
            categoryInUseSql,
            new { Slug = slug });

        if (categoryInUse)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "CATEGORY_IN_USE",
                    message = "Category cannot be deleted because it is being used by active experiences. Disable it instead."
                }
            });
        }

        const string sql = """
            update public.categories
            set
                is_deleted = true,
                is_active = false,
                deleted_at = now(),
                updated_at = now()
            where slug = @Slug
              and is_deleted = false
            returning
                name,
                slug,
                is_deleted,
                deleted_at;
        """;

        var deleted = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            Slug = slug
        });

        if (deleted is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "CATEGORY_NOT_FOUND",
                    message = "Category was not found."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                deleted = true,
                category = deleted
            },
            message = "Category deleted logically."
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

    private async Task<bool> IsPlatformAdminAsync(Guid userId)
    {
        const string sql = """
            select exists (
                select 1
                from public.profiles
                where id = @UserId
                  and role = 'platform_admin'
            );
        """;

        return await _connection.ExecuteScalarAsync<bool>(sql, new
        {
            UserId = userId
        });
    }

    private IActionResult? ValidateCreateRequest(CreateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Name is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Slug is required."
                }
            });
        }

        return null;
    }

    private IActionResult? ValidateUpdateRequest(UpdateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Name is required."
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