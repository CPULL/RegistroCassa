using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace RegistroCassa.Migrations
{
    /// <inheritdoc />
    public partial class AddClosureDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClosureDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Sede = table.Column<string>(type: "varchar(255)", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "varchar(255)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosureDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosureDays_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClosureDays_CreatedByUserId",
                table: "ClosureDays",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosureDays_Date_Sede",
                table: "ClosureDays",
                columns: new[] { "Date", "Sede" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosureDays");
        }
    }
}
