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
                entity.Property(x => x.QuestionnaireId)
                      .HasMaxLength(8)
                      .IsRequired();
                entity.Property(x => x.User)
                      .IsRequired();
                entity.Property(x => x.CreatedAt)
                      .IsRequired();
                entity.Property(x => x.SurveyData)
                      .IsRequired();
                entity.HasIndex(x => x.QuestionnaireId);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
