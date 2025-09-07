using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static TicaTourShared.Enums;

namespace TicaTourShared.Data
{
    public class User : IdentityUser
    {
        public CompanyUser? CompanyUser { get; set; }
        public CustomerUser? CustomerUser { get; set; }
    }

    public class CompanyUser
    {
        // PK = FK a AspNetUsers(Id)
        [Key]
        public string UserId { get; set; } = default!;
        public User User { get; set; } = default!;

        public int CompanyId { get; set; }
        public string Name { get; set; } = default!;
        public string Address { get; set; } = default!;
        public string Description { get; set; } = default!;
        public string ImageUrl { get; set; } = default!;
        public string PhoneNumber { get; set; } = default!;

        //Navigation properties
        // Tours
        public ICollection<Tour> Tours { get; set; } = new List<Tour>();
    }

    public class CustomerUser
    {
        [Key]
        public string UserId { get; set; } = default!;
        public User User { get; set; } = default!;

        public int CustomerId { get; set; }
        public string Name { get; set; } = default!;
        public string PhoneNumber { get; set; } = default!;
        public string IdNumber { get; set; } = default!;

        //Navigation properties
        //Bookings
        public ICollection<Booking> Bookings { get; set; }
    }
}
