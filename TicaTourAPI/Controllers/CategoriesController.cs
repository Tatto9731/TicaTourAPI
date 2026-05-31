using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace TicaTourAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly NpgsqlConnection _connection;

    public CategoriesController(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        const string sql = """
            select
                name,
                slug,
                image_url,
                is_active,
                sort_order
            from public.categories
            where is_active = true
              and is_deleted = false
            order by sort_order asc, name asc;
        """;

        var categories = await _connection.QueryAsync(sql);

        return Ok(new
        {
            data = categories.Select(c => new
            {
                name = (string)c.name,
                slug = (string)c.slug,
                imageUrl = (string?)c.image_url,
                isActive = (bool)c.is_active,
                sortOrder = (int)c.sort_order
            }),
            message = "OK"
        });
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetCategoryBySlug([FromRoute] string slug)
    {
        const string sql = """
            select
                name,
                slug,
                image_url,
                is_active,
                sort_order
            from public.categories
            where slug = @Slug
              and is_active = true
              and is_deleted = false
            limit 1;
        """;

        var category = await _connection.QueryFirstOrDefaultAsync(sql, new
        {
            Slug = slug
        });

        if (category is null)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "CATEGORY_NOT_FOUND",
                    message = "Category was not found."
                }
            });
        }

        return Ok(new
        {
            data = new
            {
                name = (string)category.name,
                slug = (string)category.slug,
                imageUrl = (string?)category.image_url,
                isActive = (bool)category.is_active,
                sortOrder = (int)category.sort_order
            },
            message = "OK"
        });
    }
}