using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static TicaTourShared.Enums;

namespace TicaTourShared
{
    public class Payment
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public DateOnly PaymentDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string TransactionId { get; set; }
        public PaymentStatus Status { get; set; }

        //Navigation properties

        //Booking
        public int BookingId { get; set; }
        public Booking Booking { get; set; }

        //Bill
        public int BillId { get; set; }
        public Bill Bill { get; set; }
    }
}
