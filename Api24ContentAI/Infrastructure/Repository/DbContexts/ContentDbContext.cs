using Api24ContentAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api24ContentAI.Infrastructure.Repository.DbContexts
{
    public class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options)
    {
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
        public DbSet<TranslationJobEntity> TranslationJobs { get; set; }
        public DbSet<DocumentTranslationChat> DocumentTranslationChats { get; set; }
        public DbSet<DocumentTranslationChatMessage> DocumentTranslationChatMessages {get; set;}
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.HasDefaultSchema("ContentDb");
            _ = modelBuilder.Entity<CustomTemplate>()
                .HasOne(static ct => ct.Marketplace)
                .WithMany()
                .HasForeignKey(static ct => ct.MarketplaceId)
                .OnDelete(DeleteBehavior.Restrict);

            _ = modelBuilder.Entity<CustomTemplate>()
                .HasOne(static ct => ct.ProductCategory)
                .WithMany()
                .HasForeignKey(static ct => ct.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            _ = modelBuilder.Entity<CustomTemplate>().HasIndex(static x => new { x.MarketplaceId, x.ProductCategoryId }).IsUnique();

            _ = modelBuilder.Entity<Template>()
                .HasOne(static t => t.ProductCategory)
                .WithMany()
                .HasForeignKey(static t => t.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            _ = modelBuilder.Entity<Template>().HasIndex(static x => new { x.ProductCategoryId }).IsUnique();

            _ = modelBuilder.Entity<RequestLog>()
                .HasOne<Marketplace>()
                .WithMany()
                .HasForeignKey(static ct => ct.MarketplaceId)
                .OnDelete(DeleteBehavior.NoAction);

            _ = modelBuilder.Entity<User>()
                .HasMany<UserRequestLog>()
                .WithOne()
                .HasForeignKey(static ct => ct.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            _ = modelBuilder.Entity<User>()
                .ToTable("Users");

            _ = modelBuilder.Entity<User>()
                .Property(static c => c.FirstName)
                .IsRequired();

            _ = modelBuilder.Entity<User>()
                .Property(static c => c.LastName)
                .IsRequired();

            _ = modelBuilder.Entity<User>()
                .Property(static c => c.UserType)
                .HasDefaultValue(UserType.Normal)
                .IsRequired();

            _ = modelBuilder.Entity<User>().HasIndex(static x => x.NormalizedUserName).IsUnique();

            _ = modelBuilder.Entity<Role>()
                .ToTable("Roles");

            _ = modelBuilder.Entity<User>().HasOne(static x => x.Role).WithMany().HasForeignKey(static x => x.RoleId);

            _ = modelBuilder.Entity<UserBalance>().HasOne(static x => x.User).WithOne(static x => x.UserBalance).HasForeignKey<UserBalance>(static x => x.UserId).IsRequired(false);

        }
    }
}
