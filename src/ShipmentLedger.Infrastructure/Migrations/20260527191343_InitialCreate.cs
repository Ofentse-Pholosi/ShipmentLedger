using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShipmentLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    ShipmentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrentStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StatusOccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Partner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.ShipmentId);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Partner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShipmentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProcessingNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "ShipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_ContentHash",
                table: "ShipmentEvents",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_Partner_EventId",
                table: "ShipmentEvents",
                columns: new[] { "Partner", "EventId" },
                unique: true,
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_ShipmentId",
                table: "ShipmentEvents",
                column: "ShipmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentEvents");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
