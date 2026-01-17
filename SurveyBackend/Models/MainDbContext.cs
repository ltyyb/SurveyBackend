using Microsoft.EntityFrameworkCore;


namespace SurveyBackend.Models
{
    public class MainDbContext : DbContext
    {
        public MainDbContext(DbContextOptions<MainDbContext> options) : base(options)
        {
        }
        // 定义 DbSet 属性
        public DbSet<User> Users { get; set; }
        public DbSet<Questionnaire> Questionnaires { get; set; }
        public DbSet<Submission> Submissions { get; set; }
        public DbSet<ReviewSubmissionData> ReviewSubmissions { get; set; }
        public DbSet<ReviewVote> ReviewVotes { get; set; }
        public DbSet<Request> Requests { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 全局设置默认 Collation
            modelBuilder.UseCollation("utf8mb4_0900_ai_ci");
            modelBuilder.HasCharSet("utf8mb4");

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

            // 配置 问卷表 实体
            modelBuilder.Entity<Questionnaire>(entity =>
            {
                entity.ToTable("questionnaires");
                entity.HasKey(x => x.QuestionnaireId);
                entity.Property(x => x.QuestionnaireId)
                      .HasMaxLength(8);
                entity.Property(x => x.FriendlyName)
                      .HasMaxLength(100)
                      .IsRequired();
                entity.Property(x => x.UniquePerUser)
                      .IsRequired();
                entity.Property(x => x.NeedReview)
                      .IsRequired();
                entity.Property(x => x.IsVerifyQuestionnaire)
                      .IsRequired();
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
                entity.Property(x => x.ShortSubmissionId)
                      .HasMaxLength(8);
                entity.Property(x => x.Questionnaire)
                      .IsRequired();
                entity.Property(x => x.User)
                      .IsRequired();
                entity.Property(x => x.CreatedAt)
                      .IsRequired();
                entity.Property(x => x.IsDisabled)
                      .IsRequired();
                entity.Property(x => x.SurveyData)
                      .IsRequired();
                entity.HasIndex(x => x.User);
            });

            // 配置 需审核提交表 实体
            modelBuilder.Entity<ReviewSubmissionData>(entity =>
            {
                entity.ToTable("review_submissions");
                entity.HasKey(x => x.ReviewSubmissionDataId);
                entity.Property(x => x.ReviewSubmissionDataId)
                      .HasMaxLength(16);
                entity.Property(x => x.Submission)
                      .IsRequired();
                entity.Property(x => x.Status)
                      .IsRequired();
                entity.Property(x => x.AIInsights);
                entity.HasIndex(x => x.Submission);
            });

            // 配置 审核投票表 实体
            modelBuilder.Entity<ReviewVote>(entity =>
            {
                entity.ToTable("review_votes");
                entity.Property(x => x.ReviewSubmissionData)
                      .IsRequired();
                entity.Property(x => x.User)
                      .IsRequired();
                entity.Property(x => x.VoteType)
                      .IsRequired();
                entity.Property(x => x.VoteTime)
                      .IsRequired();
                entity.HasIndex(x => x.ReviewSubmissionData);
            });

            modelBuilder.Entity<Request>(entity =>
            {
                entity.ToTable("requests");
                entity.HasKey(x => x.RequestId);
                entity.Property(x => x.RequestId)
                      .HasMaxLength(16);
                entity.Property(x => x.RequestType)
                      .IsRequired();
                entity.Property(x => x.User)
                      .IsRequired();
                entity.Property(x => x.IsDisabled)
                      .IsRequired();
                entity.Property(x => x.CreatedAt)
                      .IsRequired();
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
