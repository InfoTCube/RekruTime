using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RekruTime.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FormResponseLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "FormResponses",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "pl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "FormResponses");
        }
    }
}
