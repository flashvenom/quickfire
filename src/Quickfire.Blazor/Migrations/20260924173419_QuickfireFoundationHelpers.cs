using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quickfire.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class QuickfireFoundationHelpers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BridgeDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CredentialHash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BridgeDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BridgeDevices_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BridgeDevices_UserId",
                table: "BridgeDevices",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BridgeDevices");
        }
    }
}
