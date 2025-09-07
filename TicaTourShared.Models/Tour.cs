using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TicaTourShared.Data;

namespace TicaTourShared
{
    public class Tour
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Price { get; set; }
        public String Destination { get; set; }
        public List<String> Images { get; set; }
        public Boolean IsAvailable { get; set; }
        public int MaxParticipants { get; set; }
        public int CurrentParticipants { get; set; }
        //Navigation properties

        //CompanyUser
        [ForeignKey("CompanyUser")]
        public String CompanyUserId { get; set; }
        public CompanyUser CompanyUser { get; set; }

        //Bookings
        public ICollection<Booking> Bookings { get; set; }

        //Reviews
        public ICollection<Review> Reviews { get; set; }

        //Promotions
        public int PromotionId { get; set; }
        public Promotion Promotion { get; set; }
    }
}
