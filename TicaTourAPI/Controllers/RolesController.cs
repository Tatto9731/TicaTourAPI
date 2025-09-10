using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicaTourCompany.Controllers.DTOs.Auth;
using TicaTourCompany.Controllers.DTOs.Roles;
using TicaTourShared.Data;

namespace TicaTourCompany.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")] // Solo admins pueden administrar roles
public class UsersController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UsersController(UserManager<User> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        //var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user is null) return NotFound("User not found.");

        // Verifica que el rol exista (o créalo)
        if (!await _roleManager.RoleExistsAsync(dto.RoleName))
        {
            return BadRequest($"Role '{dto.RoleName}' does not exist.");
            // o: await _roleManager.CreateAsync(new IdentityRole(dto.RoleName));
        }

        var result = await _userManager.AddToRoleAsync(user, dto.RoleName);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return NoContent();
    }

    [HttpPost("remove-role")]
    public async Task<IActionResult> RemoveRole([FromBody] RemoveRoleRequest dto)
    {
        var user = await _userManager.FindByIdAsync(dto.Email);
        if (user is null) return NotFound("User not found.");

        var result = await _userManager.RemoveFromRoleAsync(user, dto.RoleName);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return NoContent();
    }

    [HttpGet("{userId}/roles")]
    public async Task<ActionResult<string[]>> GetUserRoles(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound("User not found.");

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(roles.ToArray());
    }

    // GET: api/roles
    [HttpGet("roles")]
    public async Task<ActionResult<IEnumerable<RoleResponse>>> GetAll()
    {
        var roles = await _roleManager.Roles
            .AsNoTracking()
            .Select(r => new RoleResponse(r.Id, r.Name!, r.NormalizedName!))
            .ToListAsync();

        return Ok(roles);
    }
}
