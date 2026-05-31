using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Me/Notifications")]
public class MeNotificationsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public MeNotificationsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] bool? isRead,
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

        const string sql = """
            select
                id,
                type,
                title,
                body,
                is_read,
                action_url,
                created_at
            from public.notifications
            where user_id = @UserId
              and (@IsRead is null or is_read = @IsRead)
            order by created_at desc
            limit @PerPage offset @Offset;
        """;

        const string countSql = """
            select count(*)
            from public.notifications
            where user_id = @UserId
              and (@IsRead is null or is_read = @IsRead);
        """;

        var parameters = new
        {
            UserId = userId.Value,
            IsRead = isRead,
            PerPage = perPage,
            Offset = offset
        };

        var notifications = await _connection.QueryAsync(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        return Ok(new
        {
            data = notifications.Select(n => new
            {
                id = (Guid)n.id,
                type = (string)n.type,
                title = (string)n.title,
                body = (string)n.body,
                isRead = (bool)n.is_read,
                actionUrl = (string?)n.action_url,
                createdAt = n.created_at
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

    [HttpPatch("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] Guid notificationId)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        const string sql = """
            update public.notifications
            set is_read = true
            where id = @NotificationId
              and user_id = @UserId
            returning
                id,
                type,
                title,
                body,
                is_read,
                action_url,
                created_at;
        """;

        var notification = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            NotificationId = notificationId
        });

        if (notification is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "NOTIFICATION_NOT_FOUND",
                    message = "Notification was not found."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                id = (Guid)notification.id,
                type = (string)notification.type,
                title = (string)notification.title,
                body = (string)notification.body,
                isRead = (bool)notification.is_read,
                actionUrl = (string?)notification.action_url,
                createdAt = notification.created_at
            },
            message = "Notification marked as read."
        });
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        const string sql = """
            update public.notifications
            set is_read = true
            where user_id = @UserId
              and is_read = false;
        """;

        var affected = await _connection.ExecuteAsync(sql, new
        {
            UserId = userId.Value
        });

        return Ok(new
        {
            data = new
            {
                updated = affected
            },
            message = "All notifications marked as read."
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