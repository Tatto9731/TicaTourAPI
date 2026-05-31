using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using TicaTourAPI.DTOs.Leads;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeadsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public LeadsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpPost]
    public async Task<IActionResult> CreateLead([FromBody] CreateLeadRequest request)
    {
        var validation = ValidateCreateLeadRequest(request);

        if (validation is not null)
        {
            return validation;
        }

        const string sql = """
            insert into public.leads (
                company_id,
                experience_id,
                traveler_name,
                traveler_email,
                traveler_phone,
                channel,
                status,
                estimated_value,
                currency,
                message,
                created_at,
                updated_at
            )
            select
                c.id,
                e.id,
                @TravelerName,
                @TravelerEmail,
                @TravelerPhone,
                @Channel,
                'New',
                @EstimatedValue,
                @Currency,
                @Message,
                now(),
                now()
            from public.companies c
            left join public.experiences e
                on e.company_id = c.id
               and e.slug = @ExperienceSlug
               and e.is_deleted = false
            where c.slug = @CompanySlug
              and (
                    @ExperienceSlug is null
                    or e.id is not null
                  )
            returning
                id,
                traveler_name,
                traveler_email,
                traveler_phone,
                channel,
                status,
                estimated_value,
                currency,
                message,
                created_at;
        """;

        var companySlug = request.CompanySlug;

        if (string.IsNullOrWhiteSpace(companySlug) && !string.IsNullOrWhiteSpace(request.ExperienceSlug))
        {
            companySlug = await GetCompanySlugByExperienceSlugAsync(request.ExperienceSlug);
        }

        if (string.IsNullOrWhiteSpace(companySlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug is required when ExperienceSlug is not provided or cannot be resolved."
                }
            });
        }

        var created = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            CompanySlug = companySlug,
            ExperienceSlug = string.IsNullOrWhiteSpace(request.ExperienceSlug) ? null : request.ExperienceSlug,
            request.TravelerName,
            request.TravelerEmail,
            request.TravelerPhone,
            request.Channel,
            request.EstimatedValue,
            request.Currency,
            request.Message
        });

        if (created is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "LEAD_TARGET_NOT_FOUND",
                    message = "Company or experience was not found."
                }
            });
        }

        return Ok(new
        {
            data = created,
            message = "Lead created successfully."
        });
    }

    private async Task<string?> GetCompanySlugByExperienceSlugAsync(string experienceSlug)
    {
        const string sql = """
            select c.slug
            from public.experiences e
            inner join public.companies c on c.id = e.company_id
            where e.slug = @ExperienceSlug
              and e.status = 'Published'
              and e.is_deleted = false
            limit 1;
        """;

        return await _connection.ExecuteScalarAsync<string?>(sql, new
        {
            ExperienceSlug = experienceSlug
        });
    }

    private IActionResult? ValidateCreateLeadRequest(CreateLeadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanySlug) &&
            string.IsNullOrWhiteSpace(request.ExperienceSlug))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "CompanySlug or ExperienceSlug is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.TravelerName))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "TravelerName is required."
                }
            });
        }

        if (string.IsNullOrWhiteSpace(request.TravelerEmail) &&
            string.IsNullOrWhiteSpace(request.TravelerPhone))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "TravelerEmail or TravelerPhone is required."
                }
            });
        }

        if (request.Channel is not ("Marketplace" or "WhatsApp" or "Campaign"))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_CHANNEL",
                    message = "Channel must be Marketplace, WhatsApp or Campaign."
                }
            });
        }

        if (request.Currency is not ("CRC" or "USD"))
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

        if (request.EstimatedValue < 0)
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_ESTIMATED_VALUE",
                    message = "EstimatedValue cannot be negative."
                }
            });
        }

        return null;
    }
}