using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicaTourShared.Data;

namespace TicaTourCompany.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CompanyUsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CompanyUsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/CompanyUsers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanyUser>>> GetCompanyUsers()
        {
            return await _context.CompanyUsers.ToListAsync();
        }

        // GET: api/CompanyUsers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<CompanyUser>> GetCompanyUser(string id)
        {
            var companyUser = await _context.CompanyUsers.FindAsync(id);

            if (companyUser == null)
            {
                return NotFound();
            }

            return companyUser;
        }

        // PUT: api/CompanyUsers/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
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
        [HttpPost]
        public async Task<ActionResult<CompanyUser>> PostCompanyUser(CompanyUser companyUser)
        {
            _context.CompanyUsers.Add(companyUser);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (CompanyUserExists(companyUser.UserId))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetCompanyUser", new { id = companyUser.UserId }, companyUser);
        }

        // DELETE: api/CompanyUsers/5
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
