using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Bookings")]
public class MeBookingsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public MeBookingsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyBookings(
        [FromQuery] string status = "all",
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        var statusFilter = status.ToLowerInvariant() switch
        {
            "upcoming" => "and b.status in ('Pending', 'Confirmed')",
            "past" => "and b.status in ('Completed', 'Cancelled')",
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
                b.created_at,

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title,
                e.main_image_url as experience_image,

                c.name as company_name,
                c.slug as company_slug
            from public.bookings b
            inner join public.experiences e on e.id = b.experience_id
            inner join public.companies c on c.id = b.company_id
            where b.user_id = @UserId
              {statusFilter}
            order by b.created_at desc
            limit @PerPage offset @Offset;
        """;

        var countSql = $"""
            select count(*)
            from public.bookings b
            where b.user_id = @UserId
              {statusFilter};
        """;

        var parameters = new
        {
            UserId = userId.Value,
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
                createdAt = b.created_at,
                experience = new
                {
                    publicCode = (string)b.experience_public_code,
                    slug = (string)b.experience_slug,
                    title = (string)b.experience_title,
                    image = (string?)b.experience_image
                },
                company = new
                {
                    name = (string)b.company_name,
                    slug = (string)b.company_slug
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
    public async Task<IActionResult> GetMyBookingDetail([FromRoute] string bookingCode)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
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

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title,
                e.main_image_url as experience_image,
                e.province as experience_province,
                e.zone as experience_zone,

                c.name as company_name,
                c.slug as company_slug,
                c.phone as company_phone,
                c.whatsapp as company_whatsapp,
                c.email as company_email
            from public.bookings b
            inner join public.experiences e on e.id = b.experience_id
            inner join public.companies c on c.id = b.company_id
            where b.user_id = @UserId
              and b.booking_code = @BookingCode
            limit 1;
        """;

        var booking = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            BookingCode = bookingCode
        });

        if (booking is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "BOOKING_NOT_FOUND",
                    message = "Booking was not found."
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
                experience = new
                {
                    publicCode = (string)booking.experience_public_code,
                    slug = (string)booking.experience_slug,
                    title = (string)booking.experience_title,
                    image = (string?)booking.experience_image,
                    province = (string)booking.experience_province,
                    zone = (string)booking.experience_zone
                },
                company = new
                {
                    name = (string)booking.company_name,
                    slug = (string)booking.company_slug,
                    phone = (string?)booking.company_phone,
                    whatsapp = (string?)booking.company_whatsapp,
                    email = (string)booking.company_email
                }
            },
            message = "OK"
        });
    }

    [HttpPatch("{bookingCode}/cancel")]
    public async Task<IActionResult> CancelMyBooking([FromRoute] string bookingCode)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        const string sql = """
            update public.bookings
            set
                status = 'Cancelled',
                updated_at = now()
            where user_id = @UserId
              and booking_code = @BookingCode
              and status in ('Pending', 'Confirmed')
            returning booking_code, status, updated_at;
        """;

        var cancelled = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            BookingCode = bookingCode
        });

        if (cancelled is null)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "BOOKING_CANNOT_BE_CANCELLED",
                    message = "Booking was not found or cannot be cancelled."
                }
            });
        }

        return Ok(new
        {
            data = cancelled,
            message = "Booking cancelled successfully."
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