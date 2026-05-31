using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Bookings")]
public class CompanyBookingsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyBookingsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanyBookings(
        [FromQuery] string companySlug,
        [FromQuery] string status = "all",
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
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

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
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
                b.booking_date,
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

        var bookings = await _connection.QueryAsync(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        return Ok(new
        {
            data = bookings.Select(b => new
            {
                bookingCode = (string)b.booking_code,
                status = (string)b.status,
                guestsAdults = (int)b.guests_adults,
                guestsChildren = (int)b.guests_children,
                guests = (int)b.guests,
                bookingDate = b.booking_date,
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

    [HttpGet("{bookingCode}")]
    public async Task<IActionResult> GetCompanyBookingDetail(
        [FromRoute] string bookingCode,
        [FromQuery] string companySlug)
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

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            select
                b.booking_code,
                b.status,
                b.guests_adults,
                b.guests_children,
                (b.guests_adults + b.guests_children) as guests,
                b.booking_date,
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

        var booking = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            BookingCode = bookingCode,
            CompanySlug = companySlug
        });

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
                bookingDate = booking.booking_date,
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

    [HttpPatch("{bookingCode}/status")]
    public async Task<IActionResult> UpdateCompanyBookingStatus(
        [FromRoute] string bookingCode,
        [FromQuery] string companySlug,
        [FromBody] UpdateCompanyBookingStatusRequest request)
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

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
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

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            BookingCode = bookingCode,
            CompanySlug = companySlug,
            request.Status
        });

        if (updated is null)
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

        await _connection.ExecuteAsync(notificationSql, new
        {
            UserId = (Guid)updated.user_id,
            Title = title,
            Body = body,
            ActionUrl = $"/client/profile/bookings/{bookingCode}"
        });

        return Ok(new
        {
            data = updated,
            message = "Booking status updated successfully."
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