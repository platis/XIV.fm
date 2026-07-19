using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XIV.fm.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInstallationCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_credentials",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installation_credentials", x => x.installation_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installation_credentials_credential_hash",
                table: "installation_credentials",
                column: "credential_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_credentials");
        }
    }
}
