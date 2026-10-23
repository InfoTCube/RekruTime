using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RekruTime.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DecisionEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecruitId",
                table: "EmailMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecisionSentAt",
                table: "Applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_RecruitId",
                table: "EmailMessages",
                column: "RecruitId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailMessages_Recruits_RecruitId",
                table: "EmailMessages",
                column: "RecruitId",
                principalTable: "Recruits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailMessages_Recruits_RecruitId",
                table: "EmailMessages");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_RecruitId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "RecruitId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "DecisionSentAt",
                table: "Applications");
        }
    }
}
