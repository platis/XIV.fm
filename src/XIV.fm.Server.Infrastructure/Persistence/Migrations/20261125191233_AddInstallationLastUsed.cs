using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XIV.fm.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationLastUsed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_used_at",
                table: "installation_credentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_used_at",
                table: "installation_credentials");
        }
    }
}
