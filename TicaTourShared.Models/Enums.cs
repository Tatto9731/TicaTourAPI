using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TicaTourShared
{
    public class Enums
    {
        public enum UserType
        {
            Company = 1,
            Customer = 2
        }
        public enum Rating
        {
            Poor = 1,
            Fair = 2,
            Good = 3,
            VeryGood = 4,
            Excellent = 5
        }

        public enum BookingStatus
        {
            Accepted = 1,
            Pending = 2,
            Cancelled = 3
        }
        public enum PaymentMethod
        {
            Card = 1,
            Cash = 2,
            Sinpe = 3
        }
        public enum PaymentStatus
        {
            Completed = 1,
            Pending = 2,
            Failed = 3
        }
        public enum Preferences
        {
            Adventure = 1,
            Cultural = 2,
            Historical = 3,
            Nature = 4,
            Relaxation = 5,
            Wildlife = 6
        }
    }
}
