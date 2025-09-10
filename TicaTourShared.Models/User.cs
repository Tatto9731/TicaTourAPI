using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static TicaTourShared.Enums;

namespace TicaTourShared.Data
{
    public class User : IdentityUser
    {
        public UserType UserType { get; set; }
        public CompanyUser? CompanyUser { get; set; }
        public CustomerUser? CustomerUser { get; set; }
    }

    public class CompanyUser
    {
        // PK = FK a AspNetUsers(Id)
        [Key]
        public string UserId { get; set; }
        public User User { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CompanyId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public string PhoneNumber { get; set; }

        //Navigation properties
        // Tours
        public ICollection<Tour> Tours { get; set; } = new List<Tour>();
    }

    public class CustomerUser
    {
        [Key]
        public string UserId { get; set; }
        public User User { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CustomerId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string IdNumber { get; set; }
        public List<Preferences> Preferences { get; set; }

        //Navigation properties
        //Bookings
        public ICollection<Booking> Bookings { get; set; }
    }
}
