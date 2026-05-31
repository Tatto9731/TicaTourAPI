using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Admin;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Admin/Users")]
public class AdminUsersController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public AdminUsersController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? role,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
    {
        var adminUserId = GetUserId();

        if (adminUserId is null)
        {
            return UnauthorizedResponse();
        }

        if (!await IsPlatformAdminAsync(adminUserId.Value))
        {
            return Forbid();
        }

        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        const string sql = """
            select
                p.id,
                p.full_name,
                p.role,
                p.phone,
                p.avatar_url,
                p.preferred_language,
                p.preferred_currency,
                p.dark_mode,
                p.profile_completion,
                p.is_identity_verified,
                p.created_at,
                p.updated_at,
                au.email
            from public.profiles p
            left join auth.users au on au.id = p.id
            where (@Role is null or p.role = @Role)
              and (
                    @Q is null 
                    or p.full_name ilike '%' || @Q || '%'
                    or au.email ilike '%' || @Q || '%'
                  )
            order by p.created_at desc
            limit @PerPage offset @Offset;
        """;

        const string countSql = """
            select count(*)
            from public.profiles p
            left join auth.users au on au.id = p.id
            where (@Role is null or p.role = @Role)
              and (
                    @Q is null 
                    or p.full_name ilike '%' || @Q || '%'
                    or au.email ilike '%' || @Q || '%'
                  );
        """;

        var parameters = new
        {
            Role = string.IsNullOrWhiteSpace(role) ? null : role,
            Q = string.IsNullOrWhiteSpace(q) ? null : q,
            PerPage = perPage,
            Offset = offset
        };

        var users = await _connection.QueryAsync(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        return Ok(new
        {
            data = users.Select(u => new
            {
                id = (Guid)u.id,
                email = (string?)u.email,
                fullName = (string?)u.full_name,
                role = (string)u.role,
                phone = (string?)u.phone,
                avatarUrl = (string?)u.avatar_url,
                preferredLanguage = (string)u.preferred_language,
                preferredCurrency = (string)u.preferred_currency,
                darkMode = (bool)u.dark_mode,
                profileCompletion = (int)u.profile_completion,
                isIdentityVerified = (bool)u.is_identity_verified,
                createdAt = u.created_at,
                updatedAt = u.updated_at
            }),
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

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetUserById([FromRoute] Guid userId)
    {
        var adminUserId = GetUserId();

        if (adminUserId is null)
        {
            return UnauthorizedResponse();
        }

        if (!await IsPlatformAdminAsync(adminUserId.Value))
        {
            return Forbid();
        }

        const string sql = """
            select
                p.id,
                p.full_name,
                p.role,
                p.phone,
                p.avatar_url,
                p.preferred_language,
                p.preferred_currency,
                p.dark_mode,
                p.profile_completion,
                p.is_identity_verified,
                p.created_at,
                p.updated_at,
                au.email
            from public.profiles p
            left join auth.users au on au.id = p.id
            where p.id = @UserId
            limit 1;
        """;

        var user = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId
        });

        if (user is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "USER_NOT_FOUND",
                    message = "User was not found."
                }
            });
        }

        const string companiesSql = """
            select
                c.name,
                c.slug,
                cu.role as company_role,
                cu.created_at
            from public.company_users cu
            inner join public.companies c on c.id = cu.company_id
            where cu.user_id = @UserId
            order by cu.created_at desc;
        """;

        var companies = await _connection.QueryAsync(companiesSql, new
        {
            UserId = userId
        });

        return Ok(new
        {
            data = new
            {
                id = (Guid)user.id,
                email = (string?)user.email,
                fullName = (string?)user.full_name,
                role = (string)user.role,
                phone = (string?)user.phone,
                avatarUrl = (string?)user.avatar_url,
                preferredLanguage = (string)user.preferred_language,
                preferredCurrency = (string)user.preferred_currency,
                darkMode = (bool)user.dark_mode,
                profileCompletion = (int)user.profile_completion,
                isIdentityVerified = (bool)user.is_identity_verified,
                createdAt = user.created_at,
                updatedAt = user.updated_at,
                companies = companies.Select(c => new
                {
                    name = (string)c.name,
                    slug = (string)c.slug,
                    companyRole = (string)c.company_role,
                    createdAt = c.created_at
                })
            },
            message = "OK"
        });
    }

    [HttpPatch("{userId:guid}/profile")]
    public async Task<IActionResult> UpdateUserProfile(
        [FromRoute] Guid userId,
        [FromBody] UpdateAdminUserProfileRequest request)
    {
        var adminUserId = GetUserId();

        if (adminUserId is null)
        {
            return UnauthorizedResponse();
        }

        if (!await IsPlatformAdminAsync(adminUserId.Value))
        {
            return Forbid();
        }

        var validation = ValidateProfileRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        const string sql = """
            update public.profiles
            set
                full_name = @FullName,
                phone = @Phone,
                avatar_url = @AvatarUrl,
                preferred_language = @PreferredLanguage,
                preferred_currency = @PreferredCurrency,
                dark_mode = @DarkMode,
                is_identity_verified = @IsIdentityVerified,
                updated_at = now()
            where id = @UserId
            returning
                id,
                full_name,
                role,
                phone,
                avatar_url,
                preferred_language,
                preferred_currency,
                dark_mode,
                is_identity_verified,
                updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId,
            request.FullName,
            request.Phone,
            request.AvatarUrl,
            request.PreferredLanguage,
            request.PreferredCurrency,
            request.DarkMode,
            request.IsIdentityVerified
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "USER_NOT_FOUND",
                    message = "User was not found."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "User profile updated successfully."
        });
    }

    [HttpPatch("{userId:guid}/role")]
    public async Task<IActionResult> UpdateUserRole(
        [FromRoute] Guid userId,
        [FromBody] UpdateAdminUserRoleRequest request)
    {
        var adminUserId = GetUserId();

        if (adminUserId is null)
        {
            return UnauthorizedResponse();
        }

        if (!await IsPlatformAdminAsync(adminUserId.Value))
        {
            return Forbid();
        }

        if (request.Role is not ("traveler" or "company_admin" or "platform_admin"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_ROLE",
                    message = "Role must be traveler, company_admin or platform_admin."
                }
            });
        }

        const string sql = """
            update public.profiles
            set
                role = @Role,
                updated_at = now()
            where id = @UserId
            returning
                id,
                full_name,
                role,
                updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId,
            request.Role
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "USER_NOT_FOUND",
                    message = "User was not found."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "User role updated successfully."
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

    private IActionResult? ValidateProfileRequest(UpdateAdminUserProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "FullName is required."
                }
            });
        }

        if (request.PreferredLanguage is not ("es" or "en"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_LANGUAGE",
                    message = "PreferredLanguage must be es or en."
                }
            });
        }

        if (request.PreferredCurrency is not ("CRC" or "USD"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_CURRENCY",
                    message = "PreferredCurrency must be CRC or USD."
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