using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Leads;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Leads")]
public class CompanyLeadsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyLeadsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanyLeads(
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
            "New" => "and l.status = 'New'",
            "Contacted" => "and l.status = 'Contacted'",
            "Confirmed" => "and l.status = 'Confirmed'",
            "Cancelled" => "and l.status = 'Cancelled'",
            "all" => "",
            _ => ""
        };

        var sql = $"""
            select
                l.id,
                l.traveler_name,
                l.traveler_email,
                l.traveler_phone,
                l.channel,
                l.status,
                l.estimated_value,
                l.currency,
                l.message,
                l.created_at,
                l.updated_at,

                e.public_code as experience_public_code,
                e.slug as experience_slug,
                e.title as experience_title
            from public.leads l
            inner join public.companies c on c.id = l.company_id
            left join public.experiences e on e.id = l.experience_id
            where c.slug = @CompanySlug
              {statusFilter}
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by l.created_at desc
            limit @PerPage offset @Offset;
        """;

        var countSql = $"""
            select count(*)
            from public.leads l
            inner join public.companies c on c.id = l.company_id
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

        var leads = await _connection.QueryAsync(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        return Ok(new
        {
            data = leads.Select(l => new
            {
                id = (Guid)l.id,
                travelerName = (string)l.traveler_name,
                travelerEmail = (string?)l.traveler_email,
                travelerPhone = (string?)l.traveler_phone,
                channel = (string)l.channel,
                status = (string)l.status,
                estimatedValue = (decimal)l.estimated_value,
                currency = (string)l.currency,
                message = (string?)l.message,
                createdAt = l.created_at,
                updatedAt = l.updated_at,
                experience = l.experience_slug is null
                    ? null
                    : new
                    {
                        publicCode = (string?)l.experience_public_code,
                        slug = (string?)l.experience_slug,
                        title = (string?)l.experience_title
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

    [HttpPatch("{leadId:guid}/status")]
    public async Task<IActionResult> UpdateLeadStatus(
        [FromRoute] Guid leadId,
        [FromQuery] string companySlug,
        [FromBody] UpdateLeadStatusRequest request)
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

        if (request.Status is not ("New" or "Contacted" or "Confirmed" or "Cancelled"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_STATUS",
                    message = "Status must be New, Contacted, Confirmed or Cancelled."
                }
            });
        }

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            update public.leads l
            set
                status = @Status,
                updated_at = now()
            from public.companies c
            where l.company_id = c.id
              and l.id = @LeadId
              and c.slug = @CompanySlug
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                l.id,
                l.traveler_name,
                l.status,
                l.updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            LeadId = leadId,
            CompanySlug = companySlug,
            request.Status
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "LEAD_NOT_FOUND",
                    message = "Lead was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Lead status updated successfully."
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