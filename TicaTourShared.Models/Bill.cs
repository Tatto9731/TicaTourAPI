using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TicaTourShared
{
    public class Bill
    {
        public int Id { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateOnly BillDate { get; set; }
        public Boolean IsPaid { get; set; }

        //Navigation properties

        //Payment
        [ForeignKey("Payment")]
        public int PaymentId { get; set; }
        public Payment Payment { get; set; }
    }
}
