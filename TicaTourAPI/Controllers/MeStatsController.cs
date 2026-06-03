using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TicaTourAPI.Data;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Stats")]
public class MeStatsController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MeStatsController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyStats(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = """
                select
                    (
                        select count(*)::integer
                        from public.bookings
                        where user_id = @UserId
                          and status = 'Completed'
                    ) as tours_attended,

                    (
                        select count(*)::integer
                        from public.reviews
                        where user_id = @UserId
                    ) as reviews_count,

                    (
                        select count(*)::integer
                        from public.bookings
                        where user_id = @UserId
                          and status in ('Pending', 'Confirmed')
                    ) as active_bookings;
            """;

            var command = new CommandDefinition(
                sql,
                new { UserId = userId.Value },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            var stats = await connection.QueryFirstOrDefaultAsync(command);

            return Ok(new
            {
                data = new
                {
                    toursAttended = (int)(stats?.tours_attended ?? 0),
                    reviewsCount = (int)(stats?.reviews_count ?? 0),
                    activeBookings = (int)(stats?.active_bookings ?? 0)
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ME STATS ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_STATS_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("tours-attended")]
    public async Task<IActionResult> GetToursAttended(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = """
                select count(*)::integer
                from public.bookings
                where user_id = @UserId
                  and status = 'Completed';
            """;

            var command = new CommandDefinition(
                sql,
                new { UserId = userId.Value },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            var count = await connection.ExecuteScalarAsync<int>(command);

            return Ok(new
            {
                data = new
                {
                    toursAttended = count
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ME TOURS ATTENDED ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_TOURS_ATTENDED_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("reviews-count")]
    public async Task<IActionResult> GetReviewsCount(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = """
                select count(*)::integer
                from public.reviews
                where user_id = @UserId;
            """;

            var command = new CommandDefinition(
                sql,
                new { UserId = userId.Value },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            var count = await connection.ExecuteScalarAsync<int>(command);

            return Ok(new
            {
                data = new
                {
                    reviewsCount = count
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ME REVIEWS COUNT ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_REVIEWS_COUNT_ERROR",
                    message = ex.Message
                }
            });
        }
    }

    [HttpGet("active-bookings")]
    public async Task<IActionResult> GetActiveBookings(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = """
                select count(*)::integer
                from public.bookings
                where user_id = @UserId
                  and status in ('Pending', 'Confirmed');
            """;

            var command = new CommandDefinition(
                sql,
                new { UserId = userId.Value },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            var count = await connection.ExecuteScalarAsync<int>(command);

            return Ok(new
            {
                data = new
                {
                    activeBookings = count
                },
                message = "OK"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ME ACTIVE BOOKINGS ERROR:");
            Console.WriteLine(ex.ToString());

            return StatusCode(500, new
            {
                error = new
                {
                    code = "ME_ACTIVE_BOOKINGS_ERROR",
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