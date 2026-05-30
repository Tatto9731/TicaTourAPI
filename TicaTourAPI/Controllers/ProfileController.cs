using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public ProfileController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");

        var email = User.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new
            {
                message = "User ID was not found in the token."
            });
        }

        var userGuid = Guid.Parse(userId);

        const string profileSql = """
            select 
                id,
                full_name,
                role,
                phone,
                avatar_url,
                preferred_language,
                preferred_currency,
                dark_mode,
                profile_completion,
                is_identity_verified,
                created_at,
                updated_at
            from public.profiles
            where id = @UserId;
        """;

        const string companiesSql = """
            select
                c.id,
                c.name,
                c.slug,
                c.description,
                c.province,
                c.zone,
                c.logo_url,
                c.cover_image_url,
                c.phone,
                c.whatsapp,
                c.email,
                c.website_url,
                c.is_verified,
                c.rating_avg,
                c.reviews_count,
                cu.role as company_role,
                c.created_at,
                c.updated_at
            from public.company_users cu
            inner join public.companies c on c.id = cu.company_id
            where cu.user_id = @UserId
            order by c.created_at desc;
        """;

        var profile = await _connection.QueryFirstOrDefaultAsync(profileSql, new
        {
            UserId = userGuid
        });

        if (profile is null)
        {
            return NotFound(new
            {
                message = "Profile was not found for the authenticated user."
            });
        }

        var companies = await _connection.QueryAsync(companiesSql, new
        {
            UserId = userGuid
        });

        return Ok(new
        {
            data = new
            {
                userId,
                email,
                profile,
                companies
            },
            message = "OK"
        });
    }
}