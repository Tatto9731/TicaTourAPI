using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Company;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Experiences/{slug}/PickupStops")]
public class CompanyExperiencePickupStopsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyExperiencePickupStopsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetPickupStops([FromRoute] string slug)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            select
                ps.id,
                ps.place,
                ps.time_label,
                ps.latitude,
                ps.longitude,
                ps.sort_order,
                ps.created_at
            from public.pickup_stops ps
            inner join public.experiences e on e.id = ps.experience_id
            inner join public.companies c on c.id = e.company_id
            where e.slug = @Slug
              and e.is_deleted = false
              and ps.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by ps.sort_order asc, ps.created_at asc;
        """;

        var pickupStops = await _connection.QueryAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug
        });

        return Ok(new
        {
            data = pickupStops,
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreatePickupStop(
        [FromRoute] string slug,
        [FromBody] CreatePickupStopRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidatePickupStopRequest(
            request.Place,
            request.TimeLabel,
            request.Latitude,
            request.Longitude);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            insert into public.pickup_stops (
                experience_id,
                place,
                time_label,
                latitude,
                longitude,
                sort_order,
                created_at
            )
            select
                e.id,
                @Place,
                @TimeLabel,
                @Latitude,
                @Longitude,
                @SortOrder,
                now()
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where e.slug = @Slug
              and e.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                id,
                place,
                time_label,
                latitude,
                longitude,
                sort_order,
                created_at;
        """;

        var created = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            request.Place,
            request.TimeLabel,
            request.Latitude,
            request.Longitude,
            request.SortOrder
        });

        if (created is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "EXPERIENCE_NOT_FOUND",
                    message = "Experience was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = created,
            message = "Pickup stop created successfully."
        });
    }

    [HttpPatch("{pickupStopId:guid}")]
    public async Task<IActionResult> UpdatePickupStop(
        [FromRoute] string slug,
        [FromRoute] Guid pickupStopId,
        [FromBody] UpdatePickupStopRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidatePickupStopRequest(
            request.Place,
            request.TimeLabel,
            request.Latitude,
            request.Longitude);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            update public.pickup_stops ps
            set
                place = @Place,
                time_label = @TimeLabel,
                latitude = @Latitude,
                longitude = @Longitude,
                sort_order = @SortOrder
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where ps.experience_id = e.id
              and ps.id = @PickupStopId
              and e.slug = @Slug
              and e.is_deleted = false
              and ps.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                ps.id,
                ps.place,
                ps.time_label,
                ps.latitude,
                ps.longitude,
                ps.sort_order,
                ps.created_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            PickupStopId = pickupStopId,
            request.Place,
            request.TimeLabel,
            request.Latitude,
            request.Longitude,
            request.SortOrder
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "PICKUP_STOP_NOT_FOUND",
                    message = "Pickup stop was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Pickup stop updated successfully."
        });
    }

    [HttpDelete("{pickupStopId:guid}")]
    public async Task<IActionResult> SoftDeletePickupStop(
        [FromRoute] string slug,
        [FromRoute] Guid pickupStopId)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var canAccess = await UserCanAccessExperienceAsync(userId.Value, slug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            update public.pickup_stops ps
            set
                is_deleted = true,
                deleted_at = now()
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where ps.experience_id = e.id
              and ps.id = @PickupStopId
              and e.slug = @Slug
              and e.is_deleted = false
              and ps.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                ps.id,
                ps.place,
                ps.time_label;
        """;

        var deleted = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            Slug = slug,
            PickupStopId = pickupStopId
        });

        if (deleted is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "PICKUP_STOP_NOT_FOUND",
                    message = "Pickup stop was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                deleted = true,
                pickupStop = deleted
            },
            message = "Pickup stop deleted logically."
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

    private async Task<bool> UserCanAccessExperienceAsync(Guid userId, string experienceSlug)
    {
        const string sql = """
            select exists (
                select 1
                from public.experiences e
                inner join public.companies c on c.id = e.company_id
                inner join public.company_users cu on cu.company_id = c.id
                inner join public.profiles p on p.id = cu.user_id
                where e.slug = @ExperienceSlug
                  and e.is_deleted = false
                  and cu.user_id = @UserId
                  and p.role = 'company_admin'
            );
        """;

        return await _connection.ExecuteScalarAsync<bool>(sql, new
        {
            UserId = userId,
            ExperienceSlug = experienceSlug
        });
    }

    private IActionResult? ValidatePickupStopRequest(
        string place,
        string timeLabel,
        decimal? latitude,
        decimal? longitude)
    {
        if (string.IsNullOrWhiteSpace(place))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Place is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(timeLabel))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "TimeLabel is required."
                }
            });
        }

        if (latitude is not null && (latitude < -90 || latitude > 90))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_LATITUDE",
                    message = "Latitude must be between -90 and 90."
                }
            });
        }

        if (longitude is not null && (longitude < -180 || longitude > 180))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_LONGITUDE",
                    message = "Longitude must be between -180 and 180."
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