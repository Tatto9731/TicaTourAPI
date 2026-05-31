using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TicaTourAPI.DTOs.Campaigns;

namespace TicaTourAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/Company/Campaigns")]
public class CompanyCampaignsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CompanyCampaignsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaigns(
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
            "Active" => "and ca.status = 'Active'",
            "Scheduled" => "and ca.status = 'Scheduled'",
            "Paused" => "and ca.status = 'Paused'",
            "Completed" => "and ca.status = 'Completed'",
            "all" => "",
            _ => ""
        };

        var sql = $"""
            select
                ca.id,
                ca.name,
                ca.placement,
                ca.budget,
                ca.currency,
                ca.clicks,
                ca.leads_count,
                ca.status,
                ca.starts_at,
                ca.ends_at,
                ca.created_at,
                ca.updated_at
            from public.campaigns ca
            inner join public.companies c on c.id = ca.company_id
            where c.slug = @CompanySlug
              and ca.is_deleted = false
              {statusFilter}
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            order by ca.created_at desc
            limit @PerPage offset @Offset;
        """;

        var countSql = $"""
            select count(*)
            from public.campaigns ca
            inner join public.companies c on c.id = ca.company_id
            where c.slug = @CompanySlug
              and ca.is_deleted = false
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

        var campaigns = await _connection.QueryAsync(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        return Ok(new
        {
            data = campaigns.Select(c => new
            {
                id = (Guid)c.id,
                name = (string)c.name,
                placement = (string)c.placement,
                budget = (decimal)c.budget,
                currency = (string)c.currency,
                clicks = (int)c.clicks,
                leadsCount = (int)c.leads_count,
                status = (string)c.status,
                startsAt = c.starts_at,
                endsAt = c.ends_at,
                createdAt = c.created_at,
                updatedAt = c.updated_at
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

    [HttpGet("{campaignId:guid}")]
    public async Task<IActionResult> GetCampaignById(
        [FromRoute] Guid campaignId,
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
                ca.id,
                ca.name,
                ca.placement,
                ca.budget,
                ca.currency,
                ca.clicks,
                ca.leads_count,
                ca.status,
                ca.starts_at,
                ca.ends_at,
                ca.created_at,
                ca.updated_at
            from public.campaigns ca
            inner join public.companies c on c.id = ca.company_id
            where ca.id = @CampaignId
              and c.slug = @CompanySlug
              and ca.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            limit 1;
        """;

        var campaign = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            CampaignId = campaignId,
            CompanySlug = companySlug
        });

        if (campaign is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "CAMPAIGN_NOT_FOUND",
                    message = "Campaign was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                id = (Guid)campaign.id,
                name = (string)campaign.name,
                placement = (string)campaign.placement,
                budget = (decimal)campaign.budget,
                currency = (string)campaign.currency,
                clicks = (int)campaign.clicks,
                leadsCount = (int)campaign.leads_count,
                status = (string)campaign.status,
                startsAt = campaign.starts_at,
                endsAt = campaign.ends_at,
                createdAt = campaign.created_at,
                updatedAt = campaign.updated_at
            },
            message = "OK"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return UnauthorizedResponse();
        }

        var validation = ValidateCreateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, request.CompanySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            insert into public.campaigns (
                company_id,
                name,
                placement,
                budget,
                currency,
                clicks,
                leads_count,
                status,
                starts_at,
                ends_at,
                created_at,
                updated_at,
                is_deleted
            )
            select
                c.id,
                @Name,
                @Placement,
                @Budget,
                @Currency,
                0,
                0,
                @Status,
                @StartsAt,
                @EndsAt,
                now(),
                now(),
                false
            from public.companies c
            where c.slug = @CompanySlug
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                id,
                name,
                placement,
                budget,
                currency,
                clicks,
                leads_count,
                status,
                starts_at,
                ends_at,
                created_at,
                updated_at;
        """;

        var created = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            request.CompanySlug,
            request.Name,
            request.Placement,
            request.Budget,
            request.Currency,
            request.Status,
            request.StartsAt,
            request.EndsAt
        });

        if (created is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "COMPANY_NOT_FOUND",
                    message = "Company was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = created,
            message = "Campaign created successfully."
        });
    }

    [HttpPatch("{campaignId:guid}")]
    public async Task<IActionResult> UpdateCampaign(
        [FromRoute] Guid campaignId,
        [FromQuery] string companySlug,
        [FromBody] UpdateCampaignRequest request)
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

        var validation = ValidateUpdateRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        var canAccess = await UserCanAccessCompanyAsync(userId.Value, companySlug);

        if (!canAccess)
        {
            return Forbid();
        }

        const string sql = """
            update public.campaigns ca
            set
                name = @Name,
                placement = @Placement,
                budget = @Budget,
                currency = @Currency,
                status = @Status,
                starts_at = @StartsAt,
                ends_at = @EndsAt,
                updated_at = now()
            from public.companies c
            where ca.company_id = c.id
              and ca.id = @CampaignId
              and c.slug = @CompanySlug
              and ca.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                ca.id,
                ca.name,
                ca.placement,
                ca.budget,
                ca.currency,
                ca.clicks,
                ca.leads_count,
                ca.status,
                ca.starts_at,
                ca.ends_at,
                ca.created_at,
                ca.updated_at;
        """;

        var updated = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            CampaignId = campaignId,
            CompanySlug = companySlug,
            request.Name,
            request.Placement,
            request.Budget,
            request.Currency,
            request.Status,
            request.StartsAt,
            request.EndsAt
        });

        if (updated is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "CAMPAIGN_NOT_FOUND",
                    message = "Campaign was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = updated,
            message = "Campaign updated successfully."
        });
    }

    [HttpDelete("{campaignId:guid}")]
    public async Task<IActionResult> SoftDeleteCampaign(
        [FromRoute] Guid campaignId,
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
            update public.campaigns ca
            set
                is_deleted = true,
                deleted_at = now(),
                updated_at = now(),
                status = 'Paused'
            from public.companies c
            where ca.company_id = c.id
              and ca.id = @CampaignId
              and c.slug = @CompanySlug
              and ca.is_deleted = false
              and c.id in (
                  select company_id
                  from public.company_users
                  where user_id = @UserId
              )
            returning
                ca.id,
                ca.name,
                ca.status,
                ca.deleted_at;
        """;

        var deleted = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            UserId = userId.Value,
            CampaignId = campaignId,
            CompanySlug = companySlug
        });

        if (deleted is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "CAMPAIGN_NOT_FOUND",
                    message = "Campaign was not found or user does not have access."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                deleted = true,
                campaign = deleted
            },
            message = "Campaign deleted logically."
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

    private IActionResult? ValidateCreateRequest(CreateCampaignRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanySlug))
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

        return ValidateCampaignFields(
            request.Name,
            request.Placement,
            request.Budget,
            request.Currency,
            request.Status,
            request.StartsAt,
            request.EndsAt);
    }

    private IActionResult? ValidateUpdateRequest(UpdateCampaignRequest request)
    {
        return ValidateCampaignFields(
            request.Name,
            request.Placement,
            request.Budget,
            request.Currency,
            request.Status,
            request.StartsAt,
            request.EndsAt);
    }

    private IActionResult? ValidateCampaignFields(
        string name,
        string placement,
        decimal budget,
        string currency,
        string status,
        DateTime startsAt,
        DateTime endsAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Name is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(placement))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Placement is required."
                }
            });
        }

        if (budget < 0)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_BUDGET",
                    message = "Budget cannot be negative."
                }
            });
        }

        if (currency is not ("CRC" or "USD"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_CURRENCY",
                    message = "Currency must be CRC or USD."
                }
            });
        }

        if (status is not ("Active" or "Scheduled" or "Paused" or "Completed"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_STATUS",
                    message = "Status must be Active, Scheduled, Paused or Completed."
                }
            });
        }

        if (startsAt == default || endsAt == default)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "StartsAt and EndsAt are required."
                }
            });
        }

        if (startsAt >= endsAt)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_DATE_RANGE",
                    message = "StartsAt must be earlier than EndsAt."
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