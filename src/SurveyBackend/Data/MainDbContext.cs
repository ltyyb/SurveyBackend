using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SurveyBackend.Data;

public class MainDbContext : DbContext
{
    public MainDbContext(DbContextOptions<MainDbContext> options) : base(options)
    {
    }
    // 定义 DbSet 属性
    public DbSet<User> Users => Set<User>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<Questionnaire> Questionnaires => Set<Questionnaire>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<ReviewSubmissionData> ReviewSubmissions => Set<ReviewSubmissionData>();
    public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<ForceEditGrant> ForceEditGrants => Set<ForceEditGrant>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 配置 用户表 实体
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.UserId);

            entity.Property(x => x.UserId)
                  .HasMaxLength(16);
            entity.Property(x => x.QQId)
                  .HasMaxLength(16)
                  .IsRequired();
            entity.Property(x => x.UserGroup)
                  .IsRequired();
            entity.HasIndex(x => x.QQId)
                  .IsUnique();
        });
        modelBuilder.Entity<Survey>(entity =>
        {
            entity.ToTable("surveys");
            entity.HasKey(x => x.SurveyId);
            entity.Property(x => x.SurveyId)
                  .HasMaxLength(8);
            entity.Property(x => x.Title)
                  .HasMaxLength(200)
                  .IsRequired();
            entity.Property(x => x.Description)
                  .HasMaxLength(1000);
            entity.Property(x => x.UniquePerUser)
                  .IsRequired();
            entity.Property(x => x.NeedReview)
                  .IsRequired();
            entity.Property(x => x.IsVerifySurvey)
                  .IsRequired();
            entity.Property(x => x.CreatedAt)
                  .IsRequired();
        });

        // 配置 问卷表 实体
        modelBuilder.Entity<Questionnaire>(entity =>
        {
            var llmPageNamesConverter = new ValueConverter<string[]?, string?>(
                v => v == null || v.Length == 0 ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v) ? null : JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null));
            var llmPageNamesComparer = new ValueComparer<string[]?>(
                (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (acc, x) => HashCode.Combine(acc, x == null ? 0 : x.GetHashCode())),
                v => v == null ? null : v.ToArray());

            entity.ToTable("questionnaires");
            entity.HasKey(x => x.QuestionnaireId);
            entity.Property(x => x.QuestionnaireId)
                  .HasMaxLength(8);
            entity.Property(x => x.SurveyId)
                  .HasMaxLength(8)
                  .IsRequired();
            entity.HasOne(x => x.Survey)
                  .WithMany()
                  .HasForeignKey(x => x.SurveyId)
                  .OnDelete(DeleteBehavior.Cascade);
            var llmPageNamesProperty = entity.Property(x => x.LLMPageNames)
                  .HasConversion(llmPageNamesConverter);
            llmPageNamesProperty.Metadata.SetValueComparer(llmPageNamesComparer);
            entity.Property(x => x.ReleaseDate)
                  .IsRequired();
            entity.Property(x => x.SurveyJson)
                  .IsRequired();
        });

        // 配置 提交表 实体
        modelBuilder.Entity<Submission>(entity =>
        {
            entity.ToTable("submissions");
            entity.HasKey(x => x.SubmissionId);
            entity.Property(x => x.SubmissionId)
                  .HasMaxLength(16);
            entity.Ignore(x => x.ShortSubmissionId);
            entity.Property(x => x.QuestionnaireId)
                  .HasMaxLength(8)
                  .IsRequired();
            entity.Property(x => x.UserId)
                  .HasMaxLength(16)
                  .IsRequired();
            entity.HasOne(x => x.Questionnaire)
                  .WithMany()
                  .HasForeignKey(x => x.QuestionnaireId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.CreatedAt)
                  .IsRequired();
            entity.Property(x => x.IsDisabled)
                  .IsRequired();
            entity.Property(x => x.SurveyData)
                  .IsRequired();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.QuestionnaireId);
        });

        // 配置 需审核提交表 实体
        modelBuilder.Entity<ReviewSubmissionData>(entity =>
        {
            entity.ToTable("review_submissions");
            entity.HasKey(x => x.ReviewSubmissionDataId);
            entity.Property(x => x.ReviewSubmissionDataId)
                  .HasMaxLength(16);
            entity.Property(x => x.SubmissionId)
                  .HasMaxLength(16)
                  .IsRequired();
            entity.HasOne(x => x.Submission)
                  .WithMany()
                  .HasForeignKey(x => x.SubmissionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.Status)
                  .IsRequired();
            entity.Property(x => x.AIInsights);
            entity.HasIndex(x => x.SubmissionId);
        });

        // 配置 审核投票表 实体
        modelBuilder.Entity<ReviewVote>(entity =>
        {
            entity.ToTable("review_votes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ReviewSubmissionDataId)
                  .HasMaxLength(16)
                  .IsRequired();
            entity.Property(x => x.UserId)
                  .HasMaxLength(16)
                  .IsRequired();
            entity.HasOne(x => x.ReviewSubmissionData)
                  .WithMany()
                  .HasForeignKey(x => x.ReviewSubmissionDataId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.VoteType)
                  .IsRequired();
            entity.Property(x => x.VoteTime)
                  .IsRequired();
            entity.HasIndex(x => x.ReviewSubmissionDataId);
            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<Request>(entity =>
        {
            entity.ToTable("requests");
            entity.HasKey(x => x.RequestId);
            entity.Property(x => x.RequestId)
                  .HasMaxLength(16);
            entity.Property(x => x.RequestType)
                  .IsRequired();
            entity.Property(x => x.UserId)
                  .HasMaxLength(16)
                  .IsRequired();
            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.IsDisabled)
                  .IsRequired();
            entity.Property(x => x.CreatedAt)
                  .IsRequired();
        });

        modelBuilder.Entity<ForceEditGrant>(entity =>
        {
            entity.ToTable("force_edit_grants");
            entity.HasKey(x => x.RequestId);
            entity.Property(x => x.RequestId).HasMaxLength(16);
            entity.Property(x => x.TargetUserId).HasMaxLength(16).IsRequired();
            entity.Property(x => x.QuestionnaireId).HasMaxLength(8).IsRequired();
            entity.Property(x => x.SubmissionId).HasMaxLength(16);
            entity.HasOne(x => x.Request).WithOne().HasForeignKey<ForceEditGrant>(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Questionnaire).WithMany().HasForeignKey(x => x.QuestionnaireId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        var utcConverter = new ValueConverter<DateTime, DateTime>(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(string) && property.Name.EndsWith("Id", StringComparison.Ordinal))
                {
                    // Nanoid 和 QQ 号均为 ASCII，保留原数据库的大小写不敏感查询。
                    property.SetCollation("NOCASE");
                }
                if (property.GetMaxLength() is { } maxLength)
                {
                    var tableName = entity.GetTableName()!;
                    modelBuilder.Entity(entity.ClrType).ToTable(tableName, table => table.HasCheckConstraint(
                        $"CK_{tableName}_{property.Name}_Length", $"length(\"{property.Name}\") <= {maxLength}"));
                }
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
