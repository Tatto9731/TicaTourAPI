using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.Data;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Bookings")]
public class CompanyBookingsController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CompanyBookingsController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanyBookings(
        [FromQuery] string companySlug,
        [FromQuery] string status = "all",
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        if (userId is null)
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

        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        var statusFilter = status switch
        {
            "Pending" => "and b.status = 'Pending'",
            "Confirmed" => "and b.status = 'Confirmed'",
            "Cancelled" => "and b.status = 'Cancelled'",
            "Completed" => "and b.status = 'Completed'",
            "all" => "",
            _ => ""
        };

        var sql = $"""
            select
                b.booking_code,
                b.status,
                b.guests_adults,
                b.guests_children,
                (b.guests_adults + b.guests_children) as guests,
                b.booking_date::text as booking_date,
                b.slot_label,
                b.total_amount,
                b.currency,
                b.meeting_point,
                b.notes,
                b.created_at,
                b.updated_at,

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title,
                e.main_image_url as experience_image,

                p.full_name as traveler_name,
                p.phone as traveler_phone,
                au.email as traveler_email
            from public.bookings b
            inner join public.experiences e on e.id = b.experience_id
            inner join public.companies c on c.id = b.company_id
            left join public.profiles p on p.id = b.user_id
            left join auth.users au on au.id = b.user_id
            where c.slug = @CompanySlug
              {statusFilter}
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by b.created_at desc
            limit @PerPage offset @Offset;
        """;

        var countSql = $"""
            select count(*)
            from public.bookings b
            inner join public.companies c on c.id = b.company_id
            where c.slug = @CompanySlug
              {statusFilter}
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              );
        """;

        var parameters = new
        {
            UserId = userId.Value,
            CompanySlug = companySlug,
            PerPage = perPage,
            Offset = offset
        };

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var canAccess = await UserCanAccessCompanyAsync(
                connection,
                userId.Value,
                companySlug,
                cancellationToken);

            if (!canAccess)
            {
                return Forbid();
            }

            var bookings = await connection.QueryAsync(
                new CommandDefinition(
                    sql,
                    parameters,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            var total = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    countSql,
                    parameters,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            return Ok(new
            {
                data = bookings.Select(b => new
                {
                    bookingCode = (string)b.booking_code,
                    status = (string)b.status,
                    guestsAdults = (int)b.guests_adults,
                    guestsChildren = (int)b.guests_children,
                    guests = (int)b.guests,
                    bookingDate = (string?)b.booking_date,
                    slotLabel = (string)b.slot_label,
                    totalAmount = (decimal)b.total_amount,
                    currency = (string)b.currency,
                    meetingPoint = (string?)b.meeting_point,
                    notes = (string?)b.notes,
                    createdAt = b.created_at,
                    updatedAt = b.updated_at,
                    traveler = new
                    {
                        name = (string?)b.traveler_name,
                        email = (string?)b.traveler_email,
                        phone = (string?)b.traveler_phone
                    },
                    experience = new
                    {
                        publicCode = (string)b.experience_public_code,
                        slug = (string)b.experience_slug,
                        title = (string)b.experience_title,
                        image = (string?)b.experience_image
                    }
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
        catch (Exception ex)
        {
            Console.WriteLine("GET COMPANY BOOKINGS ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "GET_COMPANY_BOOKINGS_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("{bookingCode}")]
    public async Task<IActionResult> GetCompanyBookingDetail(
        [FromRoute] string bookingCode,
        [FromQuery] string companySlug,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
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

        const string sql = """
            select
                b.booking_code,
                b.status,
                b.guests_adults,
                b.guests_children,
                (b.guests_adults + b.guests_children) as guests,
                b.booking_date::text as booking_date,
                b.slot_label,
                b.total_amount,
                b.currency,
                b.meeting_point,
                b.cancellation_policy,
                b.notes,
                b.created_at,
                b.updated_at,

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title,
                e.main_image_url as experience_image,
                e.province as experience_province,
                e.zone as experience_zone,

                p.full_name as traveler_name,
                p.phone as traveler_phone,
                au.email as traveler_email
            from public.bookings b
            inner join public.experiences e on e.id = b.experience_id
            inner join public.companies c on c.id = b.company_id
            left join public.profiles p on p.id = b.user_id
            left join auth.users au on au.id = b.user_id
            where b.booking_code = @BookingCode
              and c.slug = @CompanySlug
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            limit 1;
        """;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var canAccess = await UserCanAccessCompanyAsync(
                connection,
                userId.Value,
                companySlug,
                cancellationToken);

            if (!canAccess)
            {
                return Forbid();
            }

            var booking = await connection.QueryFirstOrDefaultAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        UserId = userId.Value,
                        BookingCode = bookingCode,
                        CompanySlug = companySlug
                    },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            if (booking is null)
            {
                return NotFound(new
                {
                    error = new
                    {
                        code = "BOOKING_NOT_FOUND",
                        message = "Booking was not found or user does not have access."
                    }
                });
            }

            return Ok(new
            {
                data = new
                {
                    bookingCode = (string)booking.booking_code,
                    status = (string)booking.status,
                    guestsAdults = (int)booking.guests_adults,
                    guestsChildren = (int)booking.guests_children,
                    guests = (int)booking.guests,
                    bookingDate = (string?)booking.booking_date,
                    slotLabel = (string)booking.slot_label,
                    totalAmount = (decimal)booking.total_amount,
                    currency = (string)booking.currency,
                    meetingPoint = (string?)booking.meeting_point,
                    cancellationPolicy = (string)booking.cancellation_policy,
                    notes = (string?)booking.notes,
                    createdAt = booking.created_at,
                    updatedAt = booking.updated_at,
                    traveler = new
                    {
                        name = (string?)booking.traveler_name,
                        email = (string?)booking.traveler_email,
                        phone = (string?)booking.traveler_phone
                    },
                    experience = new
                    {
                        publicCode = (string)booking.experience_public_code,
                        slug = (string)booking.experience_slug,
                        title = (string)booking.experience_title,
                        image = (string?)booking.experience_image,
                        province = (string)booking.experience_province,
                        zone = (string)booking.experience_zone
                    }
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("GET COMPANY BOOKING DETAIL ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "GET_COMPANY_BOOKING_DETAIL_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpPatch("{bookingCode}/status")]
    public async Task<IActionResult> UpdateCompanyBookingStatus(
        [FromRoute] string bookingCode,
        [FromQuery] string companySlug,
        [FromBody] UpdateCompanyBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
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

        if (request.Status is not ("Pending" or "Confirmed" or "Cancelled" or "Completed"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_STATUS",
                    message = "Status must be Pending, Confirmed, Cancelled or Completed."
                }
            });
        }

        const string updateSql = """
            update public.bookings b
            set
                status = @Status,
                updated_at = now()
            from public.companies c
            where b.company_id = c.id
              and b.booking_code = @BookingCode
              and c.slug = @CompanySlug
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                b.booking_code,
                b.status,
                b.updated_at,
                b.user_id;
        """;

        const string notificationSql = """
            insert into public.notifications (
                user_id,
                type,
                title,
                body,
                is_read,
                action_url,
                created_at
            )
            values (
                @UserId,
                'booking',
                @Title,
                @Body,
                false,
                @ActionUrl,
                now()
            );
        """;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var canAccess = await UserCanAccessCompanyAsync(
                connection,
                userId.Value,
                companySlug,
                cancellationToken);

            if (!canAccess)
            {
                return Forbid();
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var updated = await connection.QueryFirstOrDefaultAsync(
                    new CommandDefinition(
                        updateSql,
                        new
                        {
                            UserId = userId.Value,
                            BookingCode = bookingCode,
                            CompanySlug = companySlug,
                            request.Status
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                if (updated is null)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return NotFound(new
                    {
                        error = new
                        {
                            code = "BOOKING_NOT_FOUND",
                            message = "Booking was not found or user does not have access."
                        }
                    });
                }

                var title = request.Status switch
                {
                    "Confirmed" => "Reserva confirmada",
                    "Cancelled" => "Reserva cancelada",
                    "Completed" => "Tour completado",
                    _ => "Reserva actualizada"
                };

                var body = request.Status switch
                {
                    "Confirmed" => $"Tu reserva {bookingCode} fue confirmada por la empresa.",
                    "Cancelled" => $"Tu reserva {bookingCode} fue cancelada.",
                    "Completed" => $"Tu tour de la reserva {bookingCode} fue marcado como completado. Ya puedes dejar una reseña.",
                    _ => $"Tu reserva {bookingCode} fue actualizada."
                };

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        notificationSql,
                        new
                        {
                            UserId = (Guid)updated.user_id,
                            Title = title,
                            Body = body,
                            ActionUrl = $"/client/profile/bookings/{bookingCode}"
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                return Ok(new
                {
                    data = updated,
                    message = "Booking status updated successfully."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);

                Console.WriteLine("UPDATE COMPANY BOOKING STATUS DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "UPDATE_COMPANY_BOOKING_STATUS_DATABASE_ERROR",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("UPDATE COMPANY BOOKING STATUS ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "UPDATE_COMPANY_BOOKING_STATUS_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    private Guid? GetUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<bool> UserCanAccessCompanyAsync(
        NpgsqlConnection connection,
        Guid userId,
        string companySlug,
        CancellationToken cancellationToken)
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

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    CompanySlug = companySlug
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
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