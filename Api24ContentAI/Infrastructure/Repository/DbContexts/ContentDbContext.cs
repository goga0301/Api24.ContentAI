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
        public DbSet<Language> Languages { get; set; }

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
        }
    }
}
