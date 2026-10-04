using SurveyBackend.BackgroundServices;

namespace SurveyBackend;

public class Program
{
    public static void Main(string[] args)
    {
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
        var conn = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(conn))
        {
            Console.WriteLine("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
            Console.WriteLine("\n 按 Enter 退出");
            Console.ReadLine();
            return;
        }

        builder.Services.AddDbContextPool<MainDbContext>(options =>
        {
            options.UseMySQL(conn, opt =>
            {
                opt.CommandTimeout(60);
                opt.EnableRetryOnFailure(5);
            });
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
