using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Users")]
public class CompanyUsersController : ControllerBase
{
    private readonly NpgsqlConnection _connection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CompanyUsersController(
        NpgsqlConnection connection,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _connection = connection;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanyUsers([FromQuery] string companySlug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var canManage = await UserCanManageCompanyAsync(userId.Value, companySlug);

        if (!canManage)
        {
            return Forbid();
        }

        const string sql = """
            select
                p.id,
                p.full_name,
                p.role as platform_role,
                p.phone,
                p.avatar_url,
                au.email,
                cu.role as company_role,
                cu.created_at
            from public.company_users cu
            inner join public.companies c on c.id = cu.company_id
            inner join public.profiles p on p.id = cu.user_id
            left join auth.users au on au.id = cu.user_id
            where c.slug = @CompanySlug
            order by cu.created_at asc;
        """;

        var users = await _connection.QueryAsync(sql, new
        {
            CompanySlug = companySlug
        });

        return Ok(new
        {
            data = users.Select(u => new
            {
                id = (Guid)u.id,
                fullName = (string?)u.full_name,
                email = (string?)u.email,
                phone = (string?)u.phone,
                avatarUrl = (string?)u.avatar_url,
                role = (string)u.platform_role,
                companyRole = (string)u.company_role,
                createdAt = u.created_at
            }),
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCompanyUser([FromBody] CreateCompanyUserRequest request)
    {
        var currentUserId = GetUserId();

        if (currentUserId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateCreateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        var canManage = await UserCanManageCompanyAsync(currentUserId.Value, request.CompanySlug);

        if (!canManage)
        {
            return Forbid();
        }

        var authResult = await CreateSupabaseAuthUserAsync(
            request.Email,
            request.Password,
            request.FullName,
            "company_admin");

        if (!authResult.Success)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "AUTH_USER_CREATION_FAILED",
                    message = authResult.ErrorMessage
                }
            });
        }

        await _connection.OpenAsync();
        await using var transaction = await _connection.BeginTransactionAsync();

        try
        {
            const string insertProfileSql = """
                insert into public.profiles (
                    id,
                    full_name,
                    role,
                    phone,
                    preferred_language,
                    preferred_currency,
                    profile_completion,
                    created_at,
                    updated_at
                )
                values (
                    @UserId,
                    @FullName,
                    'company_admin',
                    @Phone,
                    'es',
                    'CRC',
                    30,
                    now(),
                    now()
                );
            """;

            const string insertCompanyUserSql = """
                insert into public.company_users (
                    company_id,
                    user_id,
                    role,
                    created_at
                )
                select
                    c.id,
                    @UserId,
                    @CompanyRole,
                    now()
                from public.companies c
                where c.slug = @CompanySlug
                returning id;
            """;

            await _connection.ExecuteAsync(insertProfileSql, new
            {
                UserId = authResult.UserId,
                request.FullName,
                request.Phone
            }, transaction);

            var companyUserId = await _connection.ExecuteScalarAsync<Guid?>(
                insertCompanyUserSql,
                new
                {
                    UserId = authResult.UserId,
                    request.CompanySlug,
                    request.CompanyRole
                },
                transaction);

            if (companyUserId is null)
            {
                await transaction.RollbackAsync();
                await DeleteSupabaseAuthUserAsync(authResult.UserId);

                return NotFound(new
                {
                    error = new
                    {
                        code = "COMPANY_NOT_FOUND",
                        message = "Company was not found."
                    }
                });
            }

            await transaction.CommitAsync();

            return Ok(new
            {
                data = new
                {
                    id = authResult.UserId,
                    email = request.Email,
                    fullName = request.FullName,
                    role = "company_admin",
                    companyRole = request.CompanyRole
                },
                message = "Company user created successfully."
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            await DeleteSupabaseAuthUserAsync(authResult.UserId);

            return StatusCode(500, new
            {
                error = new
                {
                    code = "COMPANY_USER_CREATION_FAILED",
                    message = "Company user could not be created."
                }
            });
        }
    }

    [HttpPatch("{targetUserId:guid}")]
    public async Task<IActionResult> UpdateCompanyUser(
        [FromRoute] Guid targetUserId,
        [FromQuery] string companySlug,
        [FromBody] UpdateCompanyUserRequest request)
    {
        var currentUserId = GetUserId();

        if (currentUserId is null)
        {
            return UnauthorizedResponse();
        }

        if (string.IsNullOrWhiteSpace(companySlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug is required."
                }
            });
        }

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

        if (request.CompanyRole is not ("admin" or "staff"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_COMPANY_ROLE",
                    message = "CompanyRole must be admin or staff."
                }
            });
        }

        var canManage = await UserCanManageCompanyAsync(currentUserId.Value, companySlug);

        if (!canManage)
        {
            return Forbid();
        }

        const string sql = """
            update public.profiles p
            set
                full_name = @FullName,
                phone = @Phone,
                updated_at = now()
            from public.company_users cu
            inner join public.companies c on c.id = cu.company_id
            where p.id = cu.user_id
              and cu.user_id = @TargetUserId
              and c.slug = @CompanySlug
              and cu.role <> 'owner';

            update public.company_users cu
            set
                role = @CompanyRole
            from public.companies c
            where cu.company_id = c.id
              and cu.user_id = @TargetUserId
              and c.slug = @CompanySlug
              and cu.role <> 'owner'
            returning
                cu.user_id,
                cu.role;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            TargetUserId = targetUserId,
            CompanySlug = companySlug,
            request.FullName,
            request.Phone,
            request.CompanyRole
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "COMPANY_USER_NOT_FOUND",
                    message = "Company user was not found or cannot be modified."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                userId = (Guid)updated.user_id,
                companyRole = (string)updated.role
            },
            message = "Company user updated successfully."
        });
    }

    [HttpDelete("{targetUserId:guid}")]
    public async Task<IActionResult> RemoveCompanyUser(
        [FromRoute] Guid targetUserId,
        [FromQuery] string companySlug)
    {
        var currentUserId = GetUserId();

        if (currentUserId is null)
        {
            return UnauthorizedResponse();
        }

        var canManage = await UserCanManageCompanyAsync(currentUserId.Value, companySlug);

        if (!canManage)
        {
            return Forbid();
        }

        const string sql = """
            delete from public.company_users cu
            using public.companies c
            where cu.company_id = c.id
              and cu.user_id = @TargetUserId
              and c.slug = @CompanySlug
              and cu.role <> 'owner'
            returning cu.user_id, cu.role;
        """;

        var removed = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            TargetUserId = targetUserId,
            CompanySlug = companySlug
        });

        if (removed is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "COMPANY_USER_NOT_FOUND",
                    message = "Company user was not found or cannot be removed."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                removed = true,
                userId = (Guid)removed.user_id,
                companyRole = (string)removed.role
            },
            message = "Company user removed from company."
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

    private async Task<bool> UserCanManageCompanyAsync(Guid userId, string companySlug)
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
                  and cu.role in ('owner', 'admin')
            );
        """;

        return await _connection.ExecuteScalarAsync<bool>(sql, new
        {
            UserId = userId,
            CompanySlug = companySlug
        });
    }

    private IActionResult? ValidateCreateRequest(CreateCompanyUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanySlug) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug, Email, Password and FullName are required."
                }
            });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_PASSWORD",
                    message = "Password must have at least 8 characters."
                }
            });
        }

        if (request.CompanyRole is not ("admin" or "staff"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_COMPANY_ROLE",
                    message = "CompanyRole must be admin or staff."
                }
            });
        }

        return null;
    }

    private async Task<CreateAuthUserResult> CreateSupabaseAuthUserAsync(
        string email,
        string password,
        string fullName,
        string role)
    {
        var projectUrl = _configuration["Supabase:ProjectUrl"];
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            return CreateAuthUserResult.Fail("Supabase ProjectUrl or ServiceRoleKey is missing.");
        }

        var client = _httpClientFactory.CreateClient();

        var requestUrl = $"{projectUrl.TrimEnd('/')}/auth/v1/admin/users";

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        requestMessage.Headers.Add("apikey", serviceRoleKey);

        var body = new
        {
            email,
            password,
            email_confirm = true,
            user_metadata = new
            {
                full_name = fullName,
                role
            },
            app_metadata = new
            {
                role
            }
        };

        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(requestMessage);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return CreateAuthUserResult.Fail(responseContent);
        }

        using var document = JsonDocument.Parse(responseContent);

        var userIdText = document.RootElement.GetProperty("id").GetString();

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return CreateAuthUserResult.Fail("Supabase did not return a valid user id.");
        }

        return CreateAuthUserResult.Ok(userId);
    }

    private async Task DeleteSupabaseAuthUserAsync(Guid userId)
    {
        var projectUrl = _configuration["Supabase:ProjectUrl"];
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            return;
        }

        var client = _httpClientFactory.CreateClient();

        var requestUrl = $"{projectUrl.TrimEnd('/')}/auth/v1/admin/users/{userId}";

        using var requestMessage = new HttpRequestMessage(HttpMethod.Delete, requestUrl);

        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        requestMessage.Headers.Add("apikey", serviceRoleKey);

        await client.SendAsync(requestMessage);
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

    private sealed class CreateAuthUserResult
    {
        public bool Success { get; private init; }
        public Guid UserId { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static CreateAuthUserResult Ok(Guid userId)
        {
            return new CreateAuthUserResult
            {
                Success = true,
                UserId = userId
            };
        }

        public static CreateAuthUserResult Fail(string errorMessage)
        {
            return new CreateAuthUserResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}