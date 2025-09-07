using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TicaTourShared.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();
        public DbSet<CustomerUser> CustomerUsers => Set<CustomerUser>();
        public DbSet<Tour> Tours => Set<Tour>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Bill> Bills => Set<Bill>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Ajusta a tu modelo real (ejemplo de 1:1 con UserId como FK)
            builder.Entity<CompanyUser>()
                .HasOne(c => c.User)
                .WithOne(u => u.CompanyUser)
                .HasForeignKey<CompanyUser>(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CustomerUser>()
                .HasOne(c => c.User)
                .WithOne(u => u.CustomerUser)
                .HasForeignKey<CustomerUser>(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
