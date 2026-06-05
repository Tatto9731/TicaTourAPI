using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.Data;
using TicaTourAPI.DTOs.Reviews;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me")]
public class MeReviewsController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MeReviewsController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpPost("Bookings/{bookingCode}/Review")]
    public async Task<IActionResult> CreateReviewForBooking(
        [FromRoute] string bookingCode,
        [FromBody] CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateReviewRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string bookingSql = """
                    select
                        b.id,
                        b.experience_id,
                        b.status
                    from public.bookings b
                    where b.booking_code = @BookingCode
                      and b.user_id = @UserId
                    limit 1;
                """;

                var booking = await connection.QueryFirstOrDefaultAsync(
                    new CommandDefinition(
                        bookingSql,
                        new
                        {
                            BookingCode = bookingCode,
                            UserId = userId.Value
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                if (booking is null)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return NotFound(new
                    {
                        error = new
                        {
                            code = "BOOKING_NOT_FOUND",
                            message = "Booking was not found."
                        }
                    });
                }

                if ((string)booking.status != "Completed")
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return BadRequest(new
                    {
                        error = new
                        {
                            code = "BOOKING_NOT_COMPLETED",
                            message = "Only completed bookings can receive a review."
                        }
                    });
                }

                const string insertReviewSql = """
                    insert into public.reviews (
                        user_id,
                        experience_id,
                        booking_id,
                        rating,
                        comment,
                        status,
                        created_at,
                        updated_at
                    )
                    values (
                        @UserId,
                        @ExperienceId,
                        @BookingId,
                        @Rating,
                        @Comment,
                        'Published',
                        now(),
                        now()
                    )
                    returning
                        rating,
                        comment,
                        status,
                        created_at;
                """;

                var review = await connection.QueryFirstOrDefaultAsync(
                    new CommandDefinition(
                        insertReviewSql,
                        new
                        {
                            UserId = userId.Value,
                            ExperienceId = (Guid)booking.experience_id,
                            BookingId = (Guid)booking.id,
                            request.Rating,
                            request.Comment
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                const string updateExperienceRatingSql = """
                    update public.experiences e
                    set
                        rating_avg = sub.rating_avg,
                        reviews_count = sub.reviews_count,
                        updated_at = now()
                    from (
                        select
                            experience_id,
                            round(avg(rating)::numeric, 2) as rating_avg,
                            count(*)::integer as reviews_count
                        from public.reviews
                        where experience_id = @ExperienceId
                          and status = 'Published'
                        group by experience_id
                    ) sub
                    where e.id = sub.experience_id;
                """;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        updateExperienceRatingSql,
                        new
                        {
                            ExperienceId = (Guid)booking.experience_id
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                return Ok(new
                {
                    data = new
                    {
                        rating = (int)review.rating,
                        comment = (string)review.comment,
                        status = (string)review.status,
                        createdAt = review.created_at
                    },
                    message = "Review created successfully."
                });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync(cancellationToken);

                return Conflict(new
                {
                    error = new
                    {
                        code = "REVIEW_ALREADY_EXISTS",
                        message = "This booking already has a review."
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);

                Console.WriteLine("REVIEW CREATION DATABASE ERROR:");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    error = new
                    {
                        code = "REVIEW_CREATION_FAILED",
                        message = ex.Message
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("REVIEW CREATION CONNECTION ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "REVIEW_CREATION_CONNECTION_FAILED",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("Reviews")]
    public async Task<IActionResult> GetMyReviews(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        const string sql = """
            select
                r.rating,
                r.comment,
                r.status,
                r.created_at,

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title,
                e.main_image_url as experience_image,

                b.booking_code
            from public.reviews r
            inner join public.experiences e on e.id = r.experience_id
            left join public.bookings b on b.id = r.booking_id
            where r.user_id = @UserId
            order by r.created_at desc
            limit @PerPage offset @Offset;
        """;

        const string countSql = """
            select count(*)
            from public.reviews
            where user_id = @UserId;
        """;

        var parameters = new
        {
            UserId = userId.Value,
            PerPage = perPage,
            Offset = offset
        };

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var reviews = await connection.QueryAsync(
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
                data = reviews.Select(r => new
                {
                    rating = (int)r.rating,
                    comment = (string)r.comment,
                    status = (string)r.status,
                    createdAt = r.created_at,
                    bookingCode = (string?)r.booking_code,
                    experience = new
                    {
                        publicCode = (string)r.experience_public_code,
                        slug = (string)r.experience_slug,
                        title = (string)r.experience_title,
                        image = (string?)r.experience_image
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
            Console.WriteLine("GET MY REVIEWS ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "GET_MY_REVIEWS_ERROR",
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

    private IActionResult? ValidateReviewRequest(CreateReviewRequest request)
    {
        if (request.Rating < 1 || request.Rating > 5)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_RATING",
                    message = "Rating must be between 1 and 5."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Comment is required."
                }
            });
        }

        if (request.Comment.Length > 1000)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "COMMENT_TOO_LONG",
                    message = "Comment cannot exceed 1000 characters."
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