using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using TicaTourAPI.Data;
using TicaTourAPI.DTOs.Auth;
using ResetPasswordRequest = TicaTourAPI.DTOs.Auth.ResetPasswordRequest;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AuthController(
        IDbConnectionFactory connectionFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _connectionFactory = connectionFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [Authorize]
    [HttpPost("sync-social-traveler")]
    public async Task<IActionResult> SyncSocialTraveler(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
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

        var email = GetEmailFromToken();
        var fullName = GetNameFromToken() ?? email ?? "Traveler";
        var avatarUrl = GetAvatarUrlFromToken();

        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "EMAIL_NOT_FOUND",
                    message = "Email was not found in the token."
                }
            });
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string upsertProfileSql = """
                insert into public.profiles (
                    id,
                    full_name,
                    role,
                    phone,
                    avatar_url,
                    preferred_language,
                    preferred_currency,
                    dark_mode,
                    profile_completion,
                    is_identity_verified,
                    created_at,
                    updated_at
                )
                values (
                    @Id,
                    @FullName,
                    'traveler',
                    null,
                    @AvatarUrl,
                    'es',
                    'CRC',
                    false,
                    30,
                    false,
                    now(),
                    now()
                )
                on conflict (id) do update
                set
                    full_name = coalesce(public.profiles.full_name, excluded.full_name),
                    avatar_url = coalesce(public.profiles.avatar_url, excluded.avatar_url),
                    preferred_language = coalesce(public.profiles.preferred_language, excluded.preferred_language),
                    preferred_currency = coalesce(public.profiles.preferred_currency, excluded.preferred_currency),
                    updated_at = now();
            """;

                const string upsertTravelerProfileSql = """
                insert into public.traveler_profiles (
                    user_id,
                    travel_interests,
                    preferences,
                    requires_transport,
                    notification_settings,
                    search_settings,
                    location_recommendations_enabled,
                    created_at,
                    updated_at
                )
                values (
                    @UserId,
                    '[]'::jsonb,
                    '[]'::jsonb,
                    null,
                    '{}'::jsonb,
                    '{}'::jsonb,
                    false,
                    now(),
                    now()
                )
                on conflict (user_id) do nothing;
            """;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        upsertProfileSql,
                        new
                        {
                            Id = userId.Value,
                            FullName = fullName,
                            AvatarUrl = avatarUrl
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        upsertTravelerProfileSql,
                        new
                        {
                            UserId = userId.Value
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                return Ok(new
                {
                    data = new
                    {
                        email,
                        profile = new
                        {
                            fullName,
                            role = "traveler",
                            avatarUrl
                        }
                    },
                    message = "Social traveler synced successfully."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);

                Console.WriteLine("SYNC SOCIAL TRAVELER DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "SYNC_SOCIAL_TRAVELER_DATABASE_ERROR",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("SYNC SOCIAL TRAVELER CONNECTION ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "SYNC_SOCIAL_TRAVELER_CONNECTION_ERROR",
                    message = ex.Message
                }
            });
        }
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

        if (request.BirthDate.HasValue && request.BirthDate.Value.Date > DateTime.UtcNow.Date)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_BIRTH_DATE",
                    message = "BirthDate cannot be in the future."
                }
            });
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            const string existingUserSql = """
            select exists (
                select 1
                from auth.users
                where lower(email) = lower(@Email)
            );
        """;

            var emailExists = await connection.ExecuteScalarAsync<bool>(
                existingUserSql,
                new { request.Email });

            if (emailExists)
            {
                return Conflict(new
                {
                    error = new
                    {
                        code = "EMAIL_ALREADY_EXISTS",
                        message = "A user with this email already exists."
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                const string existingPhoneSql = """
                select exists (
                    select 1
                    from public.profiles
                    where phone = @Phone
                );
            """;

                var phoneExists = await connection.ExecuteScalarAsync<bool>(
                    existingPhoneSql,
                    new { request.Phone });

                if (phoneExists)
                {
                    return Conflict(new
                    {
                        error = new
                        {
                            code = "PHONE_ALREADY_EXISTS",
                            message = "A user with this phone number already exists."
                        }
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(request.IdNumber))
            {
                const string existingIdNumberSql = """
                select exists (
                    select 1
                    from public.profiles
                    where id_number = @IdNumber
                );
            """;

                var idNumberExists = await connection.ExecuteScalarAsync<bool>(
                    existingIdNumberSql,
                    new { request.IdNumber });

                if (idNumberExists)
                {
                    return Conflict(new
                    {
                        error = new
                        {
                            code = "ID_NUMBER_ALREADY_EXISTS",
                            message = "A user with this ID number already exists."
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("REGISTER TRAVELER VALIDATION ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REGISTRATION_VALIDATION_ERROR",
                    message = ex.Message
                }
            });
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

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                const string insertProfileSql = """
                insert into public.profiles (
                    id,
                    full_name,
                    role,
                    phone,
                    id_number,
                    birth_date,
                    preferred_language,
                    preferred_currency,
                    dark_mode,
                    profile_completion,
                    created_at,
                    updated_at
                )
                values (
                    @Id,
                    @FullName,
                    'traveler',
                    @Phone,
                    @IdNumber,
                    @BirthDate,
                    @PreferredLanguage,
                    @PreferredCurrency,
                    @DarkMode,
                    @ProfileCompletion,
                    now(),
                    now()
                )
                on conflict (id) do update
                set
                    full_name = excluded.full_name,
                    role = excluded.role,
                    phone = excluded.phone,
                    id_number = excluded.id_number,
                    birth_date = excluded.birth_date,
                    preferred_language = excluded.preferred_language,
                    preferred_currency = excluded.preferred_currency,
                    dark_mode = excluded.dark_mode,
                    profile_completion = excluded.profile_completion,
                    updated_at = now();
            """;

                const string insertTravelerProfileSql = """
                insert into public.traveler_profiles (
                    user_id,
                    travel_interests,
                    preferences,
                    requires_transport,
                    notification_settings,
                    search_settings,
                    location_recommendations_enabled,
                    created_at,
                    updated_at
                )
                values (
                    @UserId,
                    @Preferences::jsonb,
                    @Preferences::jsonb,
                    @RequiresTransport,
                    '{}'::jsonb,
                    '{}'::jsonb,
                    false,
                    now(),
                    now()
                )
                on conflict (user_id) do update
                set
                    travel_interests = excluded.travel_interests,
                    preferences = excluded.preferences,
                    requires_transport = excluded.requires_transport,
                    updated_at = now();
            """;

                var profileCompletion = CalculateTravelerProfileCompletion(request);
                var preferencesJson = JsonSerializer.Serialize(request.Preferences ?? []);

                await connection.ExecuteAsync(insertProfileSql, new
                {
                    Id = authUserResult.UserId,
                    request.FullName,
                    request.Phone,
                    request.IdNumber,
                    BirthDate = request.BirthDate?.Date,
                    request.PreferredLanguage,
                    request.PreferredCurrency,
                    request.DarkMode,
                    ProfileCompletion = profileCompletion
                }, transaction);

                await connection.ExecuteAsync(insertTravelerProfileSql, new
                {
                    UserId = authUserResult.UserId,
                    Preferences = preferencesJson,
                    request.RequiresTransport
                }, transaction);

                await transaction.CommitAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync();
                await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

                return Conflict(new
                {
                    error = new
                    {
                        code = "DUPLICATE_USER_DATA",
                        message = "Email, phone or ID number is already in use."
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

                Console.WriteLine("REGISTER TRAVELER DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "REGISTRATION_DATABASE_ERROR",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

            Console.WriteLine("REGISTER TRAVELER CONNECTION ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REGISTRATION_CONNECTION_ERROR",
                    message = ex.Message
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
                    email = request.Email,
                    name = request.FullName,
                    role = "traveler",
                    darkMode = request.DarkMode
                }
            },
            message = "OK"
        });
    }

    private static int CalculateTravelerProfileCompletion(RegisterTravelerRequest request)
    {
        var completion = 30;

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            completion += 10;
        }

        if (!string.IsNullOrWhiteSpace(request.IdNumber))
        {
            completion += 15;
        }

        if (request.BirthDate.HasValue)
        {
            completion += 10;
        }

        if (request.Preferences is not null && request.Preferences.Length > 0)
        {
            completion += 20;
        }

        if (request.RequiresTransport.HasValue)
        {
            completion += 5;
        }

        return Math.Min(completion, 100);
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

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var slugExists = await connection.ExecuteScalarAsync<bool>(
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
        }
        catch (Exception ex)
        {
            Console.WriteLine("REGISTER COMPANY SLUG CHECK ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "COMPANY_SLUG_CHECK_ERROR",
                    message = ex.Message
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

        Guid companyId;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

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

                await connection.ExecuteAsync(insertProfileSql, new
                {
                    Id = authUserResult.UserId,
                    request.FullName,
                    request.Phone
                }, transaction);

                companyId = await connection.ExecuteScalarAsync<Guid>(insertCompanySql, new
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

                await connection.ExecuteAsync(insertCompanyUserSql, new
                {
                    CompanyId = companyId,
                    UserId = authUserResult.UserId
                }, transaction);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

                Console.WriteLine("REGISTER COMPANY DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "REGISTRATION_DATABASE_ERROR",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            await DeleteSupabaseAuthUserAsync(authUserResult.UserId);

            Console.WriteLine("REGISTER COMPANY CONNECTION ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REGISTRATION_CONNECTION_ERROR",
                    message = ex.Message
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

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var accessToken = GetBearerToken();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Unauthorized(new
            {
                error = new
                {
                    code = "TOKEN_NOT_FOUND",
                    message = "Bearer token was not found."
                }
            });
        }

        return await SignOutFromSupabaseAsync(accessToken, "local");
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var accessToken = GetBearerToken();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Unauthorized(new
            {
                error = new
                {
                    code = "TOKEN_NOT_FOUND",
                    message = "Bearer token was not found."
                }
            });
        }

        return await SignOutFromSupabaseAsync(accessToken, "global");
    }

    [Authorize]
    [HttpPost("logout-others")]
    public async Task<IActionResult> LogoutOthers()
    {
        var accessToken = GetBearerToken();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Unauthorized(new
            {
                error = new
                {
                    code = "TOKEN_NOT_FOUND",
                    message = "Bearer token was not found."
                }
            });
        }

        return await SignOutFromSupabaseAsync(accessToken, "others");
    }

    [Authorize]
    [HttpPost("complete-social-profile")]
    public async Task<IActionResult> CompleteSocialProfile(
    [FromBody] CompleteSocialProfileRequest? request,
    CancellationToken cancellationToken)
    {
        request ??= new CompleteSocialProfileRequest();

        var userId = GetUserId();

        if (userId is null)
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

        var email = GetEmailFromToken();

        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "EMAIL_NOT_FOUND",
                    message = "Email was not found in the token."
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
                    message = "PreferredLanguage must be 'es' or 'en'."
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
                    message = "PreferredCurrency must be 'CRC' or 'USD'."
                }
            });
        }

        if (request.BirthDate.HasValue && request.BirthDate.Value.Date > DateTime.UtcNow.Date)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_BIRTH_DATE",
                    message = "BirthDate cannot be in the future."
                }
            });
        }

        var fullName = !string.IsNullOrWhiteSpace(request.FullName)
            ? request.FullName
            : GetNameFromToken() ?? email;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                const string existingPhoneSql = """
                select exists (
                    select 1
                    from public.profiles
                    where phone = @Phone
                      and id <> @UserId
                );
            """;

                var phoneExists = await connection.ExecuteScalarAsync<bool>(
                    existingPhoneSql,
                    new
                    {
                        UserId = userId.Value,
                        request.Phone
                    });

                if (phoneExists)
                {
                    return Conflict(new
                    {
                        error = new
                        {
                            code = "PHONE_ALREADY_EXISTS",
                            message = "A user with this phone number already exists."
                        }
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(request.IdNumber))
            {
                const string existingIdNumberSql = """
                select exists (
                    select 1
                    from public.profiles
                    where id_number = @IdNumber
                      and id <> @UserId
                );
            """;

                var idNumberExists = await connection.ExecuteScalarAsync<bool>(
                    existingIdNumberSql,
                    new
                    {
                        UserId = userId.Value,
                        request.IdNumber
                    });

                if (idNumberExists)
                {
                    return Conflict(new
                    {
                        error = new
                        {
                            code = "ID_NUMBER_ALREADY_EXISTS",
                            message = "A user with this ID number already exists."
                        }
                    });
                }
            }

            const string profileExistsSql = """
            select exists (
                select 1
                from public.profiles
                where id = @UserId
            );
        """;

            var profileExists = await connection.ExecuteScalarAsync<bool>(
                profileExistsSql,
                new
                {
                    UserId = userId.Value
                });

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                if (!profileExists)
                {
                    const string insertProfileSql = """
                    insert into public.profiles (
                        id,
                        full_name,
                        role,
                        phone,
                        id_number,
                        birth_date,
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
                        @IdNumber,
                        @BirthDate,
                        @PreferredLanguage,
                        @PreferredCurrency,
                        @ProfileCompletion,
                        now(),
                        now()
                    );
                """;

                    var profileCompletion = CalculateSocialTravelerProfileCompletion(request);

                    await connection.ExecuteAsync(
                        insertProfileSql,
                        new
                        {
                            Id = userId.Value,
                            FullName = fullName,
                            request.Phone,
                            request.IdNumber,
                            BirthDate = request.BirthDate?.Date,
                            request.PreferredLanguage,
                            request.PreferredCurrency,
                            ProfileCompletion = profileCompletion
                        },
                        transaction);
                }

                const string insertTravelerProfileSql = """
                insert into public.traveler_profiles (
                    user_id,
                    travel_interests,
                    preferences,
                    requires_transport,
                    notification_settings,
                    search_settings,
                    location_recommendations_enabled,
                    created_at,
                    updated_at
                )
                values (
                    @UserId,
                    @Preferences::jsonb,
                    @Preferences::jsonb,
                    @RequiresTransport,
                    '{}'::jsonb,
                    '{}'::jsonb,
                    false,
                    now(),
                    now()
                )
                on conflict (user_id) do nothing;
            """;

                var preferencesJson = JsonSerializer.Serialize(request.Preferences ?? []);

                await connection.ExecuteAsync(
                    insertTravelerProfileSql,
                    new
                    {
                        UserId = userId.Value,
                        Preferences = preferencesJson,
                        request.RequiresTransport
                    },
                    transaction);

                await transaction.CommitAsync(cancellationToken);

                return Ok(new
                {
                    data = new
                    {
                        completed = true,
                        profileAlreadyExisted = profileExists,
                        email,
                        role = "traveler"
                    },
                    message = profileExists
                        ? "Social profile already exists."
                        : "Social profile completed successfully."
                });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync(cancellationToken);

                return Conflict(new
                {
                    error = new
                    {
                        code = "DUPLICATE_USER_DATA",
                        message = "Phone or ID number is already in use."
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);

                Console.WriteLine("COMPLETE SOCIAL PROFILE DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "COMPLETE_SOCIAL_PROFILE_DATABASE_ERROR",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("COMPLETE SOCIAL PROFILE ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "COMPLETE_SOCIAL_PROFILE_ERROR",
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

    private string? GetEmailFromToken()
    {
        return User.FindFirstValue(ClaimTypes.Email)
               ?? User.FindFirstValue("email");
    }

    private string? GetAvatarUrlFromToken()
    {
        return User.FindFirstValue("avatar_url")
               ?? User.FindFirstValue("picture")
               ?? GetUserMetadataValue("avatar_url")
               ?? GetUserMetadataValue("picture");
    }

    private string? GetUserMetadataValue(string key)
    {
        var metadataJson =
            User.FindFirstValue("user_metadata")
            ?? User.FindFirstValue("raw_user_meta_data");

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty(key, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();
        }
        catch
        {
            return null;
        }
    }

    private string? GetNameFromToken()
    {
        return User.FindFirstValue(ClaimTypes.Name)
               ?? User.FindFirstValue("name")
               ?? User.FindFirstValue("full_name");
    }

    private static int CalculateSocialTravelerProfileCompletion(CompleteSocialProfileRequest request)
    {
        var completion = 30;

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            completion += 10;
        }

        if (!string.IsNullOrWhiteSpace(request.IdNumber))
        {
            completion += 15;
        }

        if (request.BirthDate.HasValue)
        {
            completion += 10;
        }

        if (request.Preferences is not null && request.Preferences.Length > 0)
        {
            completion += 20;
        }

        if (request.RequiresTransport.HasValue)
        {
            completion += 5;
        }

        return Math.Min(completion, 100);
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

    private string? GetBearerToken()
    {
        var authorizationHeader = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";

        if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorizationHeader[bearerPrefix.Length..].Trim();
    }

    private async Task<IActionResult> SignOutFromSupabaseAsync(string accessToken, string scope)
    {
        var projectUrl = _configuration["Supabase:ProjectUrl"];
        var anonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            return StatusCode(500, new
            {
                error = new
                {
                    code = "SUPABASE_CONFIG_MISSING",
                    message = "Supabase ProjectUrl or AnonKey is missing."
                }
            });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var requestUrl = $"{projectUrl.TrimEnd('/')}/auth/v1/logout?scope={scope}";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);

            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            requestMessage.Headers.Add("apikey", anonKey);

            using var response = await client.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("SUPABASE LOGOUT ERROR:");
                Console.WriteLine(responseContent);

                return StatusCode((int)response.StatusCode, new
                {
                    error = new
                    {
                        code = "SUPABASE_LOGOUT_FAILED",
                        message = responseContent
                    }
                });
            }

            return Ok(new
            {
                data = new
                {
                    signedOut = true,
                    scope
                },
                message = "Session closed successfully."
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("LOGOUT ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "LOGOUT_ERROR",
                    message = ex.Message
                }
            });
        }
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
            Console.WriteLine("LOGIN AFTER REGISTER FAILED:");
            Console.WriteLine(responseContent);
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

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
    [FromBody] DTOs.Auth.ForgotPasswordRequest request,
    CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Email is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.RedirectTo))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "RedirectTo is required."
                }
            });
        }

        var allowedRedirects = new[]
        {
        "https://crisfx5045.github.io/demo-travel-system/reset-password",
        "http://localhost:5097/reset-password"
    };

        if (!allowedRedirects.Contains(request.RedirectTo))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_REDIRECT_URL",
                    message = "Redirect URL is not allowed."
                }
            });
        }

        var projectUrl = _configuration["Supabase:ProjectUrl"];
        var anonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            return StatusCode(500, new
            {
                error = new
                {
                    code = "SUPABASE_CONFIG_MISSING",
                    message = "Supabase configuration is missing."
                }
            });
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{projectUrl.TrimEnd('/')}/auth/v1/recover?redirect_to={Uri.EscapeDataString(request.RedirectTo)}");
            
            httpRequest.Headers.Add("apikey", anonKey);
            
            httpRequest.Content = JsonContent.Create(new
            {
                email = request.Email.Trim()
            });

            var httpClient = _httpClientFactory.CreateClient();

            var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("FORGOT PASSWORD ERROR:");
                Console.WriteLine(responseBody);

                return StatusCode((int)response.StatusCode, new
                {
                    error = new
                    {
                        code = "PASSWORD_RECOVERY_FAILED",
                        message = "Password recovery email could not be sent.",
                        details = responseBody
                    }
                });
            }

            return Ok(new
            {
                data = new
                {
                    email = request.Email.Trim()
                },
                message = "Password recovery email sent successfully."
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("FORGOT PASSWORD EXCEPTION:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "PASSWORD_RECOVERY_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [Authorize]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
    [FromBody] ResetPasswordRequest request,
    CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "NewPassword is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "ConfirmPassword is required."
                }
            });
        }

        if (request.NewPassword.Length < 8)
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

        if (request.NewPassword != request.ConfirmPassword)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "PASSWORDS_DO_NOT_MATCH",
                    message = "NewPassword and ConfirmPassword do not match."
                }
            });
        }

        var accessToken = GetBearerToken();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Unauthorized(new
            {
                error = new
                {
                    code = "TOKEN_NOT_FOUND",
                    message = "Bearer token was not found."
                }
            });
        }

        var projectUrl = _configuration["Supabase:ProjectUrl"];
        var anonKey = _configuration["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            return StatusCode(500, new
            {
                error = new
                {
                    code = "SUPABASE_CONFIG_MISSING",
                    message = "Supabase ProjectUrl or AnonKey is missing."
                }
            });
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"{projectUrl.TrimEnd('/')}/auth/v1/user");

            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Headers.Add("apikey", anonKey);

            httpRequest.Content = JsonContent.Create(new
            {
                password = request.NewPassword
            });

            var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("RESET PASSWORD ERROR:");
                Console.WriteLine(responseBody);

                return StatusCode((int)response.StatusCode, new
                {
                    error = new
                    {
                        code = "RESET_PASSWORD_FAILED",
                        message = "Password could not be updated.",
                        details = responseBody
                    }
                });
            }

            return Ok(new
            {
                data = new
                {
                    passwordUpdated = true
                },
                message = "Password updated successfully."
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("RESET PASSWORD EXCEPTION:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "RESET_PASSWORD_ERROR",
                    message = ex.Message
                }
            });
        }
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
