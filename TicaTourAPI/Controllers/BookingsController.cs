using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Bookings;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public BookingsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateCreateBookingRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        const string experienceSql = """
            select
                e.id as experience_id,
                e.company_id,
                e.price,
                e.price_currency,
                e.title,
                e.slug
            from public.experiences e
            where e.slug = @ExperienceSlug
              and e.status = 'Published'
              and e.is_deleted = false
            limit 1;
        """;

        var experience = await _connection.QueryFirstOrDefaultAsync(experienceSql, new
        {
            request.ExperienceSlug
        });

        if (experience is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "EXPERIENCE_NOT_FOUND",
                    message = "Experience was not found or is not available for booking."
                }
            });
        }

        var totalGuests = request.GuestsAdults + request.GuestsChildren;
        decimal totalAmount = (decimal)experience.price * totalGuests;

        var bookingCode = GenerateBookingCode();

        const string insertSql = """
            insert into public.bookings (
                booking_code,
                user_id,
                experience_id,
                company_id,
                status,
                guests_adults,
                guests_children,
                booking_date,
                slot_label,
                total_amount,
                currency,
                meeting_point,
                cancellation_policy,
                notes,
                created_at,
                updated_at
            )
            values (
                @BookingCode,
                @UserId,
                @ExperienceId,
                @CompanyId,
                'Pending',
                @GuestsAdults,
                @GuestsChildren,
                @BookingDate,
                @SlotLabel,
                @TotalAmount,
                @Currency,
                @MeetingPoint,
                'Cancelación gratis hasta 24 horas antes del tour.',
                @Notes,
                now(),
                now()
            )
            returning
                booking_code,
                status,
                guests_adults,
                guests_children,
                booking_date,
                slot_label,
                total_amount,
                currency,
                meeting_point,
                notes,
                created_at;
        """;

        try
        {
            var created = await _connection.QueryFirstOrDefaultAsync(insertSql, new
            {
                BookingCode = bookingCode,
                UserId = userId.Value,
                ExperienceId = (Guid)experience.experience_id,
                CompanyId = (Guid)experience.company_id,
                request.GuestsAdults,
                request.GuestsChildren,
                BookingDate = request.BookingDate?.Date,
                request.SlotLabel,
                TotalAmount = totalAmount,
                Currency = (string)experience.price_currency,
                request.MeetingPoint,
                request.Notes
            });

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
                'Reserva creada',
                @Body,
                false,
                @ActionUrl,
                now()
            );
            """;

            await _connection.ExecuteAsync(notificationSql, new
            {
                UserId = userId.Value,
                Body = $"Tu reserva {bookingCode} fue creada correctamente y está pendiente de confirmación.",
                ActionUrl = $"/client/profile/bookings/{bookingCode}"
            });

            return Ok(new
            {
                data = created,
                message = "Booking created successfully."
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new
            {
                error = new
                {
                    code = "DUPLICATE_BOOKING_CODE",
                    message = "A booking with the generated code already exists. Please try again."
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

    private IActionResult? ValidateCreateBookingRequest(CreateBookingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExperienceSlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "ExperienceSlug is required."
                }
            });
        }

        if (request.GuestsAdults < 1)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "GuestsAdults must be at least 1."
                }
            });
        }

        if (request.GuestsChildren < 0)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "GuestsChildren cannot be negative."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.SlotLabel))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "SlotLabel is required."
                }
            });
        }

        return null;
    }

    private static string GenerateBookingCode()
    {
        var year = DateTime.UtcNow.ToString("yy");
        var random = Random.Shared.Next(100000, 999999);

        return $"CR-{random}-{year}";
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