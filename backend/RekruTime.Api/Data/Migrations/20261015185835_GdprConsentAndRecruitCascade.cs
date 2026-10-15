using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RekruTime.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GdprConsentAndRecruitCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingLinks_Recruits_RecruitId",
                table: "BookingLinks");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentAt",
                table: "Recruits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BookingLinks_Recruits_RecruitId",
                table: "BookingLinks",
                column: "RecruitId",
                principalTable: "Recruits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingLinks_Recruits_RecruitId",
                table: "BookingLinks");

            migrationBuilder.DropColumn(
                name: "ConsentAt",
                table: "Recruits");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingLinks_Recruits_RecruitId",
                table: "BookingLinks",
                column: "RecruitId",
                principalTable: "Recruits",
                principalColumn: "Id");
        }
    }
}
