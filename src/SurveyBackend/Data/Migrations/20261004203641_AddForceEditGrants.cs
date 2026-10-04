using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyBackend.Data.Migrations;

/// <inheritdoc />
public partial class AddForceEditGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "force_edit_grants",
            columns: table => new
            {
                RequestId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                TargetUserId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                QuestionnaireId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, collation: "NOCASE"),
                SubmissionId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_force_edit_grants", x => x.RequestId);
                table.CheckConstraint("CK_force_edit_grants_QuestionnaireId_Length", "length(\"QuestionnaireId\") <= 8");
                table.CheckConstraint("CK_force_edit_grants_RequestId_Length", "length(\"RequestId\") <= 16");
                table.CheckConstraint("CK_force_edit_grants_SubmissionId_Length", "length(\"SubmissionId\") <= 16");
                table.CheckConstraint("CK_force_edit_grants_TargetUserId_Length", "length(\"TargetUserId\") <= 16");
                table.ForeignKey(
                    name: "FK_force_edit_grants_questionnaires_QuestionnaireId",
                    column: x => x.QuestionnaireId,
                    principalTable: "questionnaires",
                    principalColumn: "QuestionnaireId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_force_edit_grants_requests_RequestId",
                    column: x => x.RequestId,
                    principalTable: "requests",
                    principalColumn: "RequestId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_force_edit_grants_submissions_SubmissionId",
                    column: x => x.SubmissionId,
                    principalTable: "submissions",
                    principalColumn: "SubmissionId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_force_edit_grants_users_TargetUserId",
                    column: x => x.TargetUserId,
                    principalTable: "users",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_force_edit_grants_QuestionnaireId",
            table: "force_edit_grants",
            column: "QuestionnaireId");

        migrationBuilder.CreateIndex(
            name: "IX_force_edit_grants_SubmissionId",
            table: "force_edit_grants",
            column: "SubmissionId");

        migrationBuilder.CreateIndex(
            name: "IX_force_edit_grants_TargetUserId",
            table: "force_edit_grants",
            column: "TargetUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "force_edit_grants");
    }
}
