using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XIV.fm.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLastFmAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "installation_credentials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lastfm_accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lastfm_accounts", x => x.account_id);
                });

            migrationBuilder.CreateTable(
                name: "account_link_sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_credential_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    callback_state_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    provider_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorization_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_link_sessions", x => x.session_id);
                    table.ForeignKey(
                        name: "FK_account_link_sessions_lastfm_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "lastfm_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installation_credentials_account_id",
                table: "installation_credentials",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_link_sessions_account_id",
                table: "account_link_sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_link_sessions_callback_state_hash",
                table: "account_link_sessions",
                column: "callback_state_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_link_sessions_link_credential_hash",
                table: "account_link_sessions",
                column: "link_credential_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_link_sessions_provider_token_hash",
                table: "account_link_sessions",
                column: "provider_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lastfm_accounts_normalized_name",
                table: "lastfm_accounts",
                column: "normalized_name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_installation_credentials_lastfm_accounts_account_id",
                table: "installation_credentials",
                column: "account_id",
                principalTable: "lastfm_accounts",
                principalColumn: "account_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_installation_credentials_lastfm_accounts_account_id",
                table: "installation_credentials");

            migrationBuilder.DropTable(
                name: "account_link_sessions");

            migrationBuilder.DropTable(
                name: "lastfm_accounts");

            migrationBuilder.DropIndex(
                name: "IX_installation_credentials_account_id",
                table: "installation_credentials");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "installation_credentials");
        }
    }
}
