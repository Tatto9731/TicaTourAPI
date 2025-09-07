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
    public class CustomerUsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CustomerUsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/CustomerUsers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerUser>>> GetCustomerUsers()
        {
            return await _context.CustomerUsers.ToListAsync();
        }

        // GET: api/CustomerUsers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<CustomerUser>> GetCustomerUser(string id)
        {
            var customerUser = await _context.CustomerUsers.FindAsync(id);

            if (customerUser == null)
            {
                return NotFound();
            }

            return customerUser;
        }

        // PUT: api/CustomerUsers/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCustomerUser(string id, CustomerUser customerUser)
        {
            if (id != customerUser.UserId)
            {
                return BadRequest();
            }

            _context.Entry(customerUser).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CustomerUserExists(id))
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

        // POST: api/CustomerUsers
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<CustomerUser>> PostCustomerUser(CustomerUser customerUser)
        {
            _context.CustomerUsers.Add(customerUser);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (CustomerUserExists(customerUser.UserId))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetCustomerUser", new { id = customerUser.UserId }, customerUser);
        }

        // DELETE: api/CustomerUsers/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCustomerUser(string id)
        {
            var customerUser = await _context.CustomerUsers.FindAsync(id);
            if (customerUser == null)
            {
                return NotFound();
            }

            _context.CustomerUsers.Remove(customerUser);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CustomerUserExists(string id)
        {
            return _context.CustomerUsers.Any(e => e.UserId == id);
        }
    }
}
