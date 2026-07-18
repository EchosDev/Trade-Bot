using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Trade_Bot.Models;

namespace Trade_Bot.Data
{
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
        public DbSet<GridOrder> GridOrders => Set<GridOrder>();
        public DbSet<BotStatus> BotStatuses => Set<BotStatus>();
        public DbSet<BotLog> BotLogs => Set<BotLog>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApiCredential>()
                .HasOne(c => c.User)
                .WithOne(u => u.ApiCredential)
                .HasForeignKey<ApiCredential>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GridOrder>()
                .HasOne(o => o.User)
                .WithMany(u => u.GridOrders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BotStatus>()
                .HasOne(b => b.User)
                .WithOne(u => u.BotStatus)
                .HasForeignKey<BotStatus>(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BotLog>()
                .HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GridOrder>()
                .HasIndex(o => new { o.UserId, o.Status });

            builder.Entity<BotLog>()
                .HasIndex(l => new { l.UserId, l.CreatedAt });
        }
    }
}
