using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static TicaTourShared.Enums;

namespace TicaTourShared
{
    public class Review
    {
        public int Id { get; set; }
        public string ReviewText { get; set; }
        public Rating Rating { get; set; }
        public DateTime ReviewDate { get; set; }

        //Navigation properties
        //Tour
        [ForeignKey("Tour")]
        public int TourId { get; set; }
        public Tour Tour { get; set; }
    }
}
