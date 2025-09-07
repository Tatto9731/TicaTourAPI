using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicaTourShared.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecondMig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "CustomerUsers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "CompanyUsers");

            migrationBuilder.DropColumn(
                name: "userRole",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "CustomerUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "CompanyUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "CustomerUsers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "CompanyUsers");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "CustomerUsers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "CompanyUsers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "userRole",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
