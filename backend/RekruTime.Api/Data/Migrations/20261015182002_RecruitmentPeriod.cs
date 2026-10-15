using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RekruTime.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecruitmentPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecruitmentSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    InterviewsFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    InterviewsTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentSettings", x => x.Id);
                    table.CheckConstraint("CK_RecruitmentSettings_Single", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitmentSettings");
        }
    }
}
