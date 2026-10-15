using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RekruTime.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MvpSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Availabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Availabilities", x => x.Id);
                    table.CheckConstraint("CK_Availabilities_Range", "\"End\" > \"Start\"");
                    table.ForeignKey(
                        name: "FK_Availabilities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recruits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recruits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    InterviewDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                    table.CheckConstraint("CK_Sections_Buffer", "\"BufferMinutes\" >= 0");
                    table.CheckConstraint("CK_Sections_Duration", "\"InterviewDurationMinutes\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "BookingLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecruitId = table.Column<int>(type: "integer", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingLinks_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BookingLinks_Recruits_RecruitId",
                        column: x => x.RecruitId,
                        principalTable: "Recruits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecruitId = table.Column<int>(type: "integer", nullable: false),
                    SectionId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Recruits_RecruitId",
                        column: x => x.RecruitId,
                        principalTable: "Recruits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Applications_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSections",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SectionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSections", x => new { x.UserId, x.SectionId });
                    table.ForeignKey(
                        name: "FK_UserSections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSections_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingLinkSections",
                columns: table => new
                {
                    BookingLinkId = table.Column<int>(type: "integer", nullable: false),
                    SectionsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingLinkSections", x => new { x.BookingLinkId, x.SectionsId });
                    table.ForeignKey(
                        name: "FK_BookingLinkSections_BookingLinks_BookingLinkId",
                        column: x => x.BookingLinkId,
                        principalTable: "BookingLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingLinkSections_Sections_SectionsId",
                        column: x => x.SectionsId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Interviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecruitId = table.Column<int>(type: "integer", nullable: false),
                    BookingLinkId = table.Column<int>(type: "integer", nullable: true),
                    Start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BlockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interviews", x => x.Id);
                    table.CheckConstraint("CK_Interviews_Range", "\"End\" > \"Start\" AND \"BlockedUntil\" >= \"End\"");
                    table.ForeignKey(
                        name: "FK_Interviews_BookingLinks_BookingLinkId",
                        column: x => x.BookingLinkId,
                        principalTable: "BookingLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Interviews_Recruits_RecruitId",
                        column: x => x.RecruitId,
                        principalTable: "Recruits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InterviewInterviewers",
                columns: table => new
                {
                    InterviewId = table.Column<int>(type: "integer", nullable: false),
                    InterviewersId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewInterviewers", x => new { x.InterviewId, x.InterviewersId });
                    table.ForeignKey(
                        name: "FK_InterviewInterviewers_AspNetUsers_InterviewersId",
                        column: x => x.InterviewersId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InterviewInterviewers_Interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalTable: "Interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InterviewSections",
                columns: table => new
                {
                    InterviewId = table.Column<int>(type: "integer", nullable: false),
                    SectionsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewSections", x => new { x.InterviewId, x.SectionsId });
                    table.ForeignKey(
                        name: "FK_InterviewSections_Interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalTable: "Interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InterviewSections_Sections_SectionsId",
                        column: x => x.SectionsId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_RecruitId_SectionId",
                table: "Applications",
                columns: new[] { "RecruitId", "SectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_SectionId",
                table: "Applications",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_UserId_Start",
                table: "Availabilities",
                columns: new[] { "UserId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingLinks_CreatedById",
                table: "BookingLinks",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_BookingLinks_RecruitId",
                table: "BookingLinks",
                column: "RecruitId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingLinks_Token",
                table: "BookingLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingLinkSections_SectionsId",
                table: "BookingLinkSections",
                column: "SectionsId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewInterviewers_InterviewersId",
                table: "InterviewInterviewers",
                column: "InterviewersId");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_OneBookingPerLink",
                table: "Interviews",
                column: "BookingLinkId",
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_RecruitId",
                table: "Interviews",
                column: "RecruitId");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_Start",
                table: "Interviews",
                column: "Start");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewSections_SectionsId",
                table: "InterviewSections",
                column: "SectionsId");

            migrationBuilder.CreateIndex(
                name: "IX_Recruits_Email",
                table: "Recruits",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Name",
                table: "Sections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSections_SectionId",
                table: "UserSections",
                column: "SectionId");

            // One room: active interviews (incl. buffer) can never overlap. Concurrent bookings of the
            // same slot → the second insert fails (23P01) → API returns 409.
            // ponytail: multiple rooms later → btree_gist + ("RoomId" WITH =) in this constraint.
            migrationBuilder.Sql("""
                ALTER TABLE "Interviews" ADD CONSTRAINT "EX_Interviews_NoOverlap"
                  EXCLUDE USING gist (tstzrange("Start", "BlockedUntil") WITH &&)
                  WHERE ("Status" <> 'Cancelled');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "Interviews" DROP CONSTRAINT "EX_Interviews_NoOverlap";""");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "Availabilities");

            migrationBuilder.DropTable(
                name: "BookingLinkSections");

            migrationBuilder.DropTable(
                name: "InterviewInterviewers");

            migrationBuilder.DropTable(
                name: "InterviewSections");

            migrationBuilder.DropTable(
                name: "UserSections");

            migrationBuilder.DropTable(
                name: "Interviews");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "BookingLinks");

            migrationBuilder.DropTable(
                name: "Recruits");
        }
    }
}
