using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using TicaTourAPI.Data;
using TicaTourAPI.DTOs.Me;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Profile")]
public class MeProfileController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MeProfileController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpPatch]
    public async Task<IActionResult> UpdateMyProfile(
        [FromBody] UpdateMyProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                const string phoneExistsSql = """
                    select exists (
                        select 1
                        from public.profiles
                        where phone = @Phone
                          and id <> @UserId
                    );
                """;

                var phoneExists = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(
                        phoneExistsSql,
                        new
                        {
                            UserId = userId.Value,
                            request.Phone
                        },
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

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
                const string idNumberExistsSql = """
                    select exists (
                        select 1
                        from public.profiles
                        where id_number = @IdNumber
                          and id <> @UserId
                    );
                """;

                var idNumberExists = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(
                        idNumberExistsSql,
                        new
                        {
                            UserId = userId.Value,
                            request.IdNumber
                        },
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

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

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string updateProfileSql = """
                    update public.profiles
                    set
                        full_name = @FullName,
                        phone = @Phone,
                        avatar_url = @AvatarUrl,
                        preferred_language = @PreferredLanguage,
                        preferred_currency = @PreferredCurrency,
                        dark_mode = @DarkMode,
                        id_number = @IdNumber,
                        birth_date = @BirthDate,
                        profile_completion = @ProfileCompletion,
                        updated_at = now()
                    where id = @UserId
                    returning
                        full_name,
                        role,
                        phone,
                        avatar_url,
                        preferred_language,
                        preferred_currency,
                        dark_mode,
                        profile_completion,
                        is_identity_verified,
                        id_number,
                        birth_date,
                        created_at,
                        updated_at;
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

                var profileCompletion = CalculateProfileCompletion(request);
                var preferencesJson = JsonSerializer.Serialize(request.Preferences ?? []);

                var updatedProfile = await connection.QueryFirstOrDefaultAsync(
                    new CommandDefinition(
                        updateProfileSql,
                        new
                        {
                            UserId = userId.Value,
                            request.FullName,
                            request.Phone,
                            request.AvatarUrl,
                            request.PreferredLanguage,
                            request.PreferredCurrency,
                            request.DarkMode,
                            request.IdNumber,
                            BirthDate = request.BirthDate?.Date,
                            ProfileCompletion = profileCompletion
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                if (updatedProfile is null)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return NotFound(new
                    {
                        error = new
                        {
                            code = "PROFILE_NOT_FOUND",
                            message = "Profile was not found."
                        }
                    });
                }

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        upsertTravelerProfileSql,
                        new
                        {
                            UserId = userId.Value,
                            Preferences = preferencesJson,
                            request.RequiresTransport
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                return Ok(new
                {
                    data = new
                    {
                        profile = new
                        {
                            fullName = (string?)updatedProfile.full_name,
                            role = (string)updatedProfile.role,
                            phone = (string?)updatedProfile.phone,
                            avatarUrl = (string?)updatedProfile.avatar_url,
                            preferredLanguage = (string)updatedProfile.preferred_language,
                            preferredCurrency = (string)updatedProfile.preferred_currency,
                            darkMode = (bool)updatedProfile.dark_mode,
                            profileCompletion = (int)updatedProfile.profile_completion,
                            isIdentityVerified = (bool)updatedProfile.is_identity_verified,
                            birthDate = updatedProfile.birth_date,
                            hasIdNumber = !string.IsNullOrWhiteSpace((string?)updatedProfile.id_number),
                            idNumberMasked = MaskIdNumber((string?)updatedProfile.id_number),
                            preferences = request.Preferences ?? [],
                            requiresTransport = request.RequiresTransport,
                            createdAt = updatedProfile.created_at,
                            updatedAt = updatedProfile.updated_at
                        }
                    },
                    message = "Profile updated successfully."
                });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync(cancellationToken);

                return Conflict(new
                {
                    error = new
                    {
                        code = "DUPLICATE_PROFILE_DATA",
                        message = "Phone or ID number is already in use."
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);

                Console.WriteLine("UPDATE PROFILE DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "UPDATE_PROFILE_DATABASE_ERROR",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("UPDATE PROFILE ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "UPDATE_PROFILE_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    private IActionResult? ValidateRequest(UpdateMyProfileRequest request)
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

        return null;
    }

    private static int CalculateProfileCompletion(UpdateMyProfileRequest request)
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

        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            completion += 10;
        }

        return Math.Min(completion, 100);
    }

    private static string? MaskIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            return null;
        }

        var clean = idNumber.Trim();

        if (clean.Length <= 4)
        {
            return "****";
        }

        var lastFour = clean[^4..];

        return $"****{lastFour}";
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
}