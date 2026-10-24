using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RekruTime.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MinNoticeSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinNoticeHours",
                table: "RecruitmentSettings",
                type: "integer",
                nullable: false,
                defaultValue: 12); // the old fixed notice
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinNoticeHours",
                table: "RecruitmentSettings");
        }
    }
}
