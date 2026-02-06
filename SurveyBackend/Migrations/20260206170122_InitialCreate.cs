using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace SurveyBackend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "questionnaires",
                columns: table => new
                {
                    QuestionnaireId = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    FriendlyName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UniquePerUser = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NeedReview = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsVerifyQuestionnaire = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SurveyJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questionnaires", x => x.QuestionnaireId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    QQId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    UserGroup = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.UserId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "requests",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requests", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_requests_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    SubmissionId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    QuestionnaireId = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    UserId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SurveyData = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.SubmissionId);
                    table.ForeignKey(
                        name: "FK_submissions_questionnaires_QuestionnaireId",
                        column: x => x.QuestionnaireId,
                        principalTable: "questionnaires",
                        principalColumn: "QuestionnaireId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "review_submissions",
                columns: table => new
                {
                    ReviewSubmissionDataId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    SubmissionId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    AIInsights = table.Column<string>(type: "longtext", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_submissions", x => x.ReviewSubmissionDataId);
                    table.ForeignKey(
                        name: "FK_review_submissions_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "submissions",
                        principalColumn: "SubmissionId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "review_votes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ReviewSubmissionDataId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    UserId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    VoteType = table.Column<int>(type: "int", nullable: false),
                    VoteTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_votes_review_submissions_ReviewSubmissionDataId",
                        column: x => x.ReviewSubmissionDataId,
                        principalTable: "review_submissions",
                        principalColumn: "ReviewSubmissionDataId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_votes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_requests_UserId",
                table: "requests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_review_submissions_SubmissionId",
                table: "review_submissions",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_review_votes_ReviewSubmissionDataId",
                table: "review_votes",
                column: "ReviewSubmissionDataId");

            migrationBuilder.CreateIndex(
                name: "IX_review_votes_UserId",
                table: "review_votes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_QuestionnaireId",
                table: "submissions",
                column: "QuestionnaireId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_UserId",
                table: "submissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_QQId",
                table: "users",
                column: "QQId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requests");

            migrationBuilder.DropTable(
                name: "review_votes");

            migrationBuilder.DropTable(
                name: "review_submissions");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "questionnaires");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
