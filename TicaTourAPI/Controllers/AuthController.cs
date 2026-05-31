using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TicaTourAPI.DTOs.Auth;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly NpgsqlConnection _connection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AuthController(
        NpgsqlConnection connection,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _connection = connection;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpPost("register-traveler")]
    public async Task<IActionResult> RegisterTraveler([FromBody] RegisterTravelerRequest request)
    {
        var validationError = ValidateCommonUserFields(
            request.Email,
            request.Password,
            request.FullName,
            request.PreferredLanguage,
            request.PreferredCurrency);

        if (validationError is not null)
        {
            return validationError;
        }

        var authUserResult = await CreateSupabaseAuthUserAsync(
            request.Email,
            request.Password,
            request.FullName,
            "traveler");

        if (!authUserResult.Success)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "AUTH_USER_CREATION_FAILED",
                    message = authUserResult.ErrorMessage
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
                    @Id,
                    @FullName,
                    'traveler',
                    @Phone,
                    @PreferredLanguage,
                    @PreferredCurrency,
                    30,
                    now(),
                    now()
                );
            """;

            const string insertTravelerProfileSql = """
                insert into public.traveler_profiles (
                    user_id,
                    travel_interests,
                    notification_settings,
                    search_settings,
                    location_recommendations_enabled,
                    created_at,
                    updated_at
                )
                values (
                    @UserId,
                    '[]'::jsonb,
                    '{}'::jsonb,
                    '{}'::jsonb,
                    false,
                    now(),
                    now()
                );
            """;

            await _connection.ExecuteAsync(insertProfileSql, new
            {
                Id = authUserResult.UserId,
                request.FullName,
                request.Phone,
                request.PreferredLanguage,
                request.PreferredCurrency
            }, transaction);

            await _connection.ExecuteAsync(insertTravelerProfileSql, new
            {
                UserId = authUserResult.UserId
            }, transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REGISTRATION_DATABASE_ERROR",
                    message = "The user was created in Supabase Auth, but the profile could not be created. The auth user was rolled back."
                }
            });
        }

        var loginResult = await LoginWithPasswordAsync(request.Email, request.Password);

        if (!loginResult.Success)
        {
            return Ok(new
            {
                data = new
                {
                    user = new
                    {
                        id = authUserResult.UserId,
                        email = request.Email,
                        name = request.FullName,
                        role = "traveler"
                    }
                },
                message = "User registered successfully. Login is required."
            });
        }

        return Ok(new
        {
            data = new
            {
                accessToken = loginResult.AccessToken,
                refreshToken = loginResult.RefreshToken,
                user = new
                {
                    id = authUserResult.UserId,
                    email = request.Email,
                    name = request.FullName,
                    role = "traveler"
                }
            },
            message = "OK"
        });
    }

    [HttpPost("register-company")]
    public async Task<IActionResult> RegisterCompany([FromBody] RegisterCompanyRequest request)
    {
        var validationError = ValidateCommonUserFields(
            request.Email,
            request.Password,
            request.FullName,
            "es",
            "CRC");

        if (validationError is not null)
        {
            return validationError;
        }

        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.CompanySlug) ||
            string.IsNullOrWhiteSpace(request.Province) ||
            string.IsNullOrWhiteSpace(request.Zone) ||
            string.IsNullOrWhiteSpace(request.CompanyEmail))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanyName, CompanySlug, Province, Zone and CompanyEmail are required."
                }
            });
        }

        await _connection.OpenAsync();

        var slugExists = await _connection.ExecuteScalarAsync<bool>(
            "select exists(select 1 from public.companies where slug = @Slug);",
            new { Slug = request.CompanySlug });

        if (slugExists)
        {
            return Conflict(new
            {
                error = new
                {
                    code = "COMPANY_SLUG_ALREADY_EXISTS",
                    message = "The company slug is already in use."
                }
            });
        }

        var authUserResult = await CreateSupabaseAuthUserAsync(
            request.Email,
            request.Password,
            request.FullName,
            "company_admin");

        if (!authUserResult.Success)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "AUTH_USER_CREATION_FAILED",
                    message = authUserResult.ErrorMessage
                }
            });
        }

        await using var transaction = await _connection.BeginTransactionAsync();

        Guid companyId;

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
                    @Id,
                    @FullName,
                    'company_admin',
                    @Phone,
                    'es',
                    'CRC',
                    40,
                    now(),
                    now()
                );
            """;

            const string insertCompanySql = """
                insert into public.companies (
                    name,
                    slug,
                    description,
                    province,
                    zone,
                    phone,
                    whatsapp,
                    email,
                    website_url,
                    is_verified,
                    rating_avg,
                    reviews_count,
                    created_at,
                    updated_at
                )
                values (
                    @Name,
                    @Slug,
                    @Description,
                    @Province,
                    @Zone,
                    @Phone,
                    @Whatsapp,
                    @Email,
                    @WebsiteUrl,
                    false,
                    0,
                    0,
                    now(),
                    now()
                )
                returning id;
            """;

            const string insertCompanyUserSql = """
                insert into public.company_users (
                    company_id,
                    user_id,
                    role,
                    created_at
                )
                values (
                    @CompanyId,
                    @UserId,
                    'owner',
                    now()
                );
            """;

            await _connection.ExecuteAsync(insertProfileSql, new
            {
                Id = authUserResult.UserId,
                request.FullName,
                request.Phone
            }, transaction);

            companyId = await _connection.ExecuteScalarAsync<Guid>(insertCompanySql, new
            {
                Name = request.CompanyName,
                Slug = request.CompanySlug,
                Description = request.CompanyDescription,
                request.Province,
                request.Zone,
                Phone = request.Phone,
                request.Whatsapp,
                Email = request.CompanyEmail,
                request.WebsiteUrl
            }, transaction);

            await _connection.ExecuteAsync(insertCompanyUserSql, new
            {
                CompanyId = companyId,
                UserId = authUserResult.UserId
            }, transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REGISTRATION_DATABASE_ERROR",
                    message = "The auth user was created, but the company profile could not be completed. The auth user was rolled back."
                }
            });
        }

        var loginResult = await LoginWithPasswordAsync(request.Email, request.Password);

        if (!loginResult.Success)
        {
            return Ok(new
            {
                data = new
                {
                    user = new
                    {
                        id = authUserResult.UserId,
                        email = request.Email,
                        name = request.FullName,
                        role = "company_admin"
                    },
                    company = new
                    {
                        id = companyId,
                        name = request.CompanyName,
                        slug = request.CompanySlug
                    }
                },
                message = "Company user registered successfully. Login is required."
            });
        }

        return Ok(new
        {
            data = new
            {
                accessToken = loginResult.AccessToken,
                refreshToken = loginResult.RefreshToken,
                user = new
                {
                    id = authUserResult.UserId,
                    email = request.Email,
                    name = request.FullName,
                    role = "company_admin"
                },
                company = new
                {
                    id = companyId,
                    name = request.CompanyName,
                    slug = request.CompanySlug
                }
            },
            message = "OK"
        });
    }

    private IActionResult? ValidateCommonUserFields(
        string email,
        string password,
        string fullName,
        string preferredLanguage,
        string preferredCurrency)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(fullName))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Email, Password and FullName are required."
                }
            });
        }

        if (password.Length < 8)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Password must have at least 8 characters."
                }
            });
        }

        if (preferredLanguage is not ("es" or "en"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "PreferredLanguage must be 'es' or 'en'."
                }
            });
        }

        if (preferredCurrency is not ("CRC" or "USD"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "PreferredCurrency must be 'CRC' or 'USD'."
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

    private async Task<LoginResult> LoginWithPasswordAsync(string email, string password)
    {
        var projectUrl = _configuration["Supabase:ProjectUrl"];
        var anonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            return LoginResult.Fail();
        }

        var client = _httpClientFactory.CreateClient();

        var requestUrl = $"{projectUrl.TrimEnd('/')}/auth/v1/token?grant_type=password";

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        requestMessage.Headers.Add("apikey", anonKey);

        var body = new
        {
            email,
            password
        };

        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(requestMessage);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return LoginResult.Fail();
        }

        using var document = JsonDocument.Parse(responseContent);

        var accessToken = document.RootElement.GetProperty("access_token").GetString();
        var refreshToken = document.RootElement.GetProperty("refresh_token").GetString();

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return LoginResult.Fail();
        }

        return LoginResult.Ok(accessToken, refreshToken);
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

    private sealed class LoginResult
    {
        public bool Success { get; private init; }
        public string? AccessToken { get; private init; }
        public string? RefreshToken { get; private init; }

        public static LoginResult Ok(string accessToken, string refreshToken)
        {
            return new LoginResult
            {
                Success = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };
        }

        public static LoginResult Fail()
        {
            return new LoginResult
            {
                Success = false
            };
        }
    }
}