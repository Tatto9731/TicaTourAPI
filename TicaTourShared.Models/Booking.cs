using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TicaTourShared.Data;
using static TicaTourShared.Enums;

namespace TicaTourShared
{
    public class Booking
    {
        public int Id { get; set; }
        public DateOnly BookingDate { get; set; }
        public int NumberOfParticipants { get; set; }
        public BookingStatus Status { get; set; }

        //Navigation properties

        //Tour
        public int TourId { get; set; }
        public Tour Tour { get; set; }

        //CustomerUser  
        public String CustomerUserId { get; set; }
        public CustomerUser CustomerUser { get; set; }

        //Payment
        public Payment Payment { get; set; }
        public List<Booking> Bookings { get; set; }

    }
}
