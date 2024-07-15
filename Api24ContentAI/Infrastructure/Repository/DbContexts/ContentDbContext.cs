using Api24ContentAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api24ContentAI.Infrastructure.Repository.DbContexts
{
    public class ContentDbContext : DbContext
    {
        public ContentDbContext(DbContextOptions<ContentDbContext> options) : base(options)
        {
        }

        public DbSet<CustomTemplate> CustomTemplates { get; set; }
        public DbSet<Marketplace> Marketplaces { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<Template> Templates { get; set; }
        public DbSet<RequestLog> RequestLogs { get; set; }
        public DbSet<UserRequestLog> UserRequestLogs { get; set; }
        public DbSet<Language> Languages { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserBalance> UserBalances { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("ContentDb");
            modelBuilder.Entity<CustomTemplate>()
                .HasOne(ct => ct.Marketplace)
                .WithMany()
                .HasForeignKey(ct => ct.MarketplaceId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomTemplate>()
                .HasOne(ct => ct.ProductCategory)
                .WithMany()
                .HasForeignKey(ct => ct.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomTemplate>().HasIndex(x => new { x.MarketplaceId, x.ProductCategoryId }).IsUnique();

            modelBuilder.Entity<Template>()
                .HasOne(t => t.ProductCategory)
                .WithMany()
                .HasForeignKey(t => t.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Template>().HasIndex(x => new { x.ProductCategoryId }).IsUnique();

            modelBuilder.Entity<RequestLog>()
                .HasOne<Marketplace>()
                .WithMany()
                .HasForeignKey(ct => ct.MarketplaceId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<User>()
                .HasMany<UserRequestLog>()
                .WithOne()
                .HasForeignKey(ct => ct.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<User>()
                .ToTable("Users");

            modelBuilder.Entity<User>()
                .Property(c => c.FirstName)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(c => c.LastName)
                .IsRequired();
            
            modelBuilder.Entity<User>().HasIndex(x => x.NormalizedUserName).IsUnique();

            modelBuilder.Entity<Role>()
                .ToTable("Roles");

            modelBuilder.Entity<User>().HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);

            modelBuilder.Entity<UserBalance>().HasOne(x => x.User).WithOne(x => x.UserBalance).HasForeignKey<UserBalance>(x => x.UserId).IsRequired(false);

        }
    }
}
