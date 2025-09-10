using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicaTourCompany.Controllers.DTOs.CompanyUser;
using TicaTourShared.Data;

namespace TicaTourCompany.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CompanyUsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;

        public CompanyUsersController(ApplicationDbContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: api/CompanyUsers
        [Authorize]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanyUser>>> GetCompanyUsers()
        {
            return await _context.CompanyUsers.ToListAsync();
        }

        // GET: api/CompanyUsers/5
        [Authorize]
        [HttpGet("{id}")]
        public async Task<ActionResult<CompanyUserResponse>> GetById(string id)
        {
            var e = await _context.CompanyUsers.FindAsync(id);
            if (e is null) return NotFound();

            return new CompanyUserResponse(
                e.UserId, e.CompanyId, e.Name, e.Address, e.Description, e.ImageUrl, e.PhoneNumber);
        }

        // PUT: api/CompanyUsers/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCompanyUser(string id, CompanyUser companyUser)
        {
            if (id != companyUser.UserId)
            {
                return BadRequest();
            }

            _context.Entry(companyUser).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CompanyUserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/CompanyUsers
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        // Crea Identity User + CompanyUser (sin CustomerUser, sin relaciones extra)
        [Authorize(Roles = "Admin")]
        [HttpPost("register")]
        public async Task<ActionResult<CompanyUserResponse>> RegisterCompanyUser(
            [FromBody] RegisterCompanyUserRequest dto)
        {
            // 1) Crear usuario de Identity
            var user = new User { UserName = dto.User.UserName, Email = dto.User.Email };
            var create = await _userManager.CreateAsync(user, dto.User.Password);
            if (!create.Succeeded) return BadRequest(create.Errors);

            // 2) Crear perfil CompanyUser vinculado (PK=FK)
            if (await _context.CompanyUsers.FindAsync(user.Id) is not null)
                return Conflict("Company profile already exists for this user.");

            var entity = new CompanyUser
            {
                UserId = user.Id,
                Name = dto.CompanyUser.Name,
                Address = dto.CompanyUser.Address,
                Description = dto.CompanyUser.Description,
                ImageUrl = dto.CompanyUser.ImageUrl,
                PhoneNumber = dto.CompanyUser.PhoneNumber
            };

            _context.CompanyUsers.Add(entity);
            await _context.SaveChangesAsync();

            var response = new CompanyUserResponse(
                entity.UserId, entity.CompanyId, entity.Name, entity.Address,
                entity.Description, entity.ImageUrl, entity.PhoneNumber);

            return CreatedAtAction(nameof(GetById), new { id = entity.UserId }, response);
        }



        // DELETE: api/CompanyUsers/5
        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCompanyUser(string id)
        {
            var companyUser = await _context.CompanyUsers.FindAsync(id);
            if (companyUser == null)
            {
                return NotFound();
            }

            _context.CompanyUsers.Remove(companyUser);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CompanyUserExists(string id)
        {
            return _context.CompanyUsers.Any(e => e.UserId == id);
        }
    }
}
