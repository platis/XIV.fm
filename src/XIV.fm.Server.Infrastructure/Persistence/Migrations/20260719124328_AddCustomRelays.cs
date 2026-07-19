using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core generates constant index-column arrays.

namespace XIV.fm.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomRelays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relays",
                columns: table => new
                {
                    relay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relays", x => x.relay_id);
                    table.ForeignKey(
                        name: "FK_relays_lastfm_accounts_owner_account_id",
                        column: x => x.owner_account_id,
                        principalTable: "lastfm_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relay_invitations",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relay_invitations", x => x.invitation_id);
                    table.ForeignKey(
                        name: "FK_relay_invitations_lastfm_accounts_accepted_by_account_id",
                        column: x => x.accepted_by_account_id,
                        principalTable: "lastfm_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relay_invitations_relays_relay_id",
                        column: x => x.relay_id,
                        principalTable: "relays",
                        principalColumn: "relay_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "relay_memberships",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relay_memberships", x => x.membership_id);
                    table.ForeignKey(
                        name: "FK_relay_memberships_lastfm_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "lastfm_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relay_memberships_relays_relay_id",
                        column: x => x.relay_id,
                        principalTable: "relays",
                        principalColumn: "relay_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "relay_removals",
                columns: table => new
                {
                    relay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relay_removals", x => new { x.relay_id, x.account_id });
                    table.ForeignKey(
                        name: "FK_relay_removals_lastfm_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "lastfm_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relay_removals_relays_relay_id",
                        column: x => x.relay_id,
                        principalTable: "relays",
                        principalColumn: "relay_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_relay_invitations_accepted_by_account_id",
                table: "relay_invitations",
                column: "accepted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_relay_invitations_relay_id",
                table: "relay_invitations",
                column: "relay_id");

            migrationBuilder.CreateIndex(
                name: "IX_relay_invitations_token_hash",
                table: "relay_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_relay_memberships_account_id",
                table: "relay_memberships",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_relay_memberships_relay_id_account_id",
                table: "relay_memberships",
                columns: new[] { "relay_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_relay_removals_account_id",
                table: "relay_removals",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_relays_owner_account_id_created_at",
                table: "relays",
                columns: new[] { "owner_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_relays_owner_account_id_idempotency_key",
                table: "relays",
                columns: new[] { "owner_account_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relay_invitations");

            migrationBuilder.DropTable(
                name: "relay_memberships");

            migrationBuilder.DropTable(
                name: "relay_removals");

            migrationBuilder.DropTable(
                name: "relays");
        }
    }
}
