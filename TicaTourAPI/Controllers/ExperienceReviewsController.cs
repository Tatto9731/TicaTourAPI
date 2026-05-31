using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/Experiences/{slug}/Reviews")]
public class ExperienceReviewsController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public ExperienceReviewsController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetExperienceReviews(
        [FromRoute] string slug,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 20)
    {
        page = page <= 0 ? 1 : page;
        perPage = perPage <= 0 || perPage > 100 ? 20 : perPage;

        var offset = (page - 1) * perPage;

        const string sql = """
            select
                r.id,
                r.rating,
                r.comment,
                r.created_at,

                p.full_name as traveler_name,
                p.avatar_url as traveler_avatar_url
            from public.reviews r
            inner join public.experiences e on e.id = r.experience_id
            left join public.profiles p on p.id = r.user_id
            where e.slug = @Slug
              and e.status = 'Published'
              and e.is_deleted = false
              and r.status = 'Published'
            order by r.created_at desc
            limit @PerPage offset @Offset;
        """;

        const string countSql = """
            select count(*)
            from public.reviews r
            inner join public.experiences e on e.id = r.experience_id
            where e.slug = @Slug
              and e.status = 'Published'
              and e.is_deleted = false
              and r.status = 'Published';
        """;

        var parameters = new
        {
            Slug = slug,
            PerPage = perPage,
            Offset = offset
        };

        var reviews = await _connection.QueryAsync(sql, parameters);
        var total = await _connection.ExecuteScalarAsync<int>(countSql, parameters);

        return Ok(new
        {
            data = reviews.Select(r => new
            {
                id = (Guid)r.id,
                rating = (int)r.rating,
                comment = (string)r.comment,
                createdAt = r.created_at,
                traveler = new
                {
                    name = (string?)r.traveler_name,
                    avatarUrl = (string?)r.traveler_avatar_url
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
}