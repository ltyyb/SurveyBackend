using SurveyBackend.BackgroundServices;

namespace SurveyBackend;

public class Program
{
    public static void Main(string[] args)
    {
        if (DatabaseCommands.TryRun(args, out var exitCode))
        {
            Environment.ExitCode = exitCode;
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        // Add services to the container.

        builder.Services.AddControllers();
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Services.AddOptions<BotOptions>()
            .BindConfiguration(BotOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.AccessToken), "Bot:accessToken 未配置。")
            .Validate(options => options.WsPort is > 0 and <= 65535, "Bot:wsPort 必须是 1 到 65535 之间的端口号。")
            .Validate(options => options.MainGroupId > 0, "Bot:mainGroupId 必须是有效的正整数群号。")
            .Validate(options => options.VerifyGroupId > 0, "Bot:verifyGroupId 必须是有效的正整数群号。")
            .Validate(options => options.AdminId > 0, "Bot:adminId 必须是有效的正整数 QQ 号。")
            .ValidateOnStart();
        builder.Services.AddOptions<ApiOptions>()
            .BindConfiguration(ApiOptions.SectionName)
            .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _), "API:Endpoint 必须是有效的绝对 URL。")
            .Validate(options => Uri.TryCreate(options.SurveyLinkEndpoint, UriKind.Absolute, out _), "API:SurveyLinkEndpoint 必须是有效的绝对 URL。")
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ReviewOptions>, ReviewOptionsValidator>();
        builder.Services.AddOptions<ReviewOptions>()
            .BindConfiguration(ReviewOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddOptions<LlmOptions>()
            .BindConfiguration(LlmOptions.SectionName);
        builder.Services.AddOptions<ApplicationOptions>()
            .Bind(builder.Configuration);
        if (builder.Configuration.GetConnectionString("DefaultConnection") is not null)
        {
            throw new InvalidOperationException("已移除 MySQL 支持。请删除 ConnectionStrings:DefaultConnection 并配置 Database:Path。");
        }

        var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new();
        var databasePath = databaseOptions.GetFullPath(AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        builder.Services.AddDbContextPool<MainDbContext>(options =>
        {
            options.UseSqlite(SqliteDatabase.ConnectionString(databasePath), opt => opt.CommandTimeout(60));
            if (builder.Environment.IsDevelopment())
            {
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
                options.LogTo(Console.WriteLine, LogLevel.Information);
            }
        });
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });
        builder.Services.AddSingleton<IOnebotService, OnebotService>();
        builder.Services.AddSingleton<IHostedService>(sp =>
            (OnebotService)sp.GetRequiredService<IOnebotService>());
        builder.Services.AddSingleton<IHostedService, BackgroundPushingService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IHostedService, BackgroundVerifyService>();


        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            SqliteDatabase.Initialize(scope.ServiceProvider.GetRequiredService<MainDbContext>());
        }
        app.Logger.LogInformation("SQLite 数据库: {DatabasePath}", databasePath);
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        app.UseRouting();
        app.UseCors("AllowAll");
        app.UseHttpsRedirection();

        app.UseAuthorization();


        app.MapControllers();

        app.Run();

        // 设置每分钟自动重新加载问卷

    }

}
