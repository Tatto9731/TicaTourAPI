using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TicaTourShared
{
    public class Promotion
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public decimal DiscountPercentage { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public Boolean IsActive { get; set; }

        //Navigation properties
        //Tours
        [ForeignKey("Tour")]
        public int TourId { get; set; }
        public Tour Tour { get; set; }
    }
}
