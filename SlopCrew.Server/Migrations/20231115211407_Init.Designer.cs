// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SlopCrew.Server.Database;

#nullable disable

namespace SlopCrew.Server.Migrations
{
    [DbContext(typeof(SlopDbContext))]
    [Migration("20231115211407_Init")]
    partial class Init
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "7.0.14");

            modelBuilder.Entity("SlopCrew.Server.Database.User", b =>
                {
                    b.Property<string>("DiscordId")
                        .HasColumnType("TEXT");

                    b.Property<string>("DiscordRefreshToken")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("DiscordToken")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DiscordTokenExpires")
                        .HasColumnType("TEXT");

                    b.Property<string>("DiscordUsername")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("GameToken")
                        .HasColumnType("TEXT");

                    b.HasKey("DiscordId");

                    b.ToTable("Users");
                });
#pragma warning restore 612, 618
        }
    }
}
