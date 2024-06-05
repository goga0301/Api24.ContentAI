﻿// <auto-generated />
using System;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Api24ContentAI.Migrations
{
    [DbContext(typeof(ContentDbContext))]
    [Migration("20240605183046_LimitOnMarketplace")]
    partial class LimitOnMarketplace
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("ContentDb")
                .HasAnnotation("Relational:MaxIdentifierLength", 63)
                .HasAnnotation("ProductVersion", "5.0.17")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.CustomTemplate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("MarketplaceId")
                        .HasColumnType("uuid");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<Guid>("ProductCategoryId")
                        .HasColumnType("uuid");

                    b.Property<string>("Text")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("ProductCategoryId");

                    b.HasIndex("MarketplaceId", "ProductCategoryId")
                        .IsUnique();

                    b.ToTable("CustomTemplates");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.Language", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("Languages");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.Marketplace", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<int>("Limit")
                        .HasColumnType("integer");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("Marketplaces");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.ProductCategory", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Api24Id")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<string>("NameEng")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("ProductCategories");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.RequestLog", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreateTime")
                        .HasColumnType("timestamp without time zone");

                    b.Property<Guid>("MarketplaceId")
                        .HasColumnType("uuid");

                    b.Property<string>("RequestJson")
                        .HasColumnType("text");

                    b.Property<int>("RequestType")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("MarketplaceId");

                    b.ToTable("RequestLogs");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.Template", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<Guid>("ProductCategoryId")
                        .HasColumnType("uuid");

                    b.Property<string>("Text")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("ProductCategoryId")
                        .IsUnique();

                    b.ToTable("Templates");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.CustomTemplate", b =>
                {
                    b.HasOne("Api24ContentAI.Domain.Entities.Marketplace", "Marketplace")
                        .WithMany()
                        .HasForeignKey("MarketplaceId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("Api24ContentAI.Domain.Entities.ProductCategory", "ProductCategory")
                        .WithMany()
                        .HasForeignKey("ProductCategoryId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Marketplace");

                    b.Navigation("ProductCategory");
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.RequestLog", b =>
                {
                    b.HasOne("Api24ContentAI.Domain.Entities.Marketplace", null)
                        .WithMany()
                        .HasForeignKey("MarketplaceId")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();
                });

            modelBuilder.Entity("Api24ContentAI.Domain.Entities.Template", b =>
                {
                    b.HasOne("Api24ContentAI.Domain.Entities.ProductCategory", "ProductCategory")
                        .WithMany()
                        .HasForeignKey("ProductCategoryId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("ProductCategory");
                });
#pragma warning restore 612, 618
        }
    }
}
