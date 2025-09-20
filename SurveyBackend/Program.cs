using SurveyBackend.Controllers;
namespace SurveyBackend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

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
            builder.Services.AddSingleton<IHostedService, BackgroundVerifyService>();


            var app = builder.Build();

            SurveyPkgInstance? surveyPkg;


            // 初始化检查和 Load
            using (var scope = app.Services.CreateScope())
            {
                var surveyLogger = scope.ServiceProvider.GetRequiredService<ILogger<SurveyController>>();
                var mainLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                if (string.IsNullOrEmpty(app.Configuration.GetConnectionString("DefaultConnection")))
                {
                    mainLogger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                if (string.IsNullOrEmpty(app.Configuration["Bot:mainGroupId"]))
                {
                    mainLogger.LogError("主群组群号未配置。请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为主群组群号。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                else
                {
                    if (!long.TryParse(app.Configuration["Bot:mainGroupId"], out long mainGroupId))
                    {
                        mainLogger.LogError($"主群组群号配置无效，无法将 \"{app.Configuration["Bot:mainGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:mainGroupId\" 为正确的群号。");
                        Console.WriteLine("\n 按 Enter 退出");
                        Console.ReadLine();
                        return;
                    }
                }
                if (string.IsNullOrEmpty(app.Configuration["Bot:adminId"]))
                {
                    mainLogger.LogError("管理员QQ号未配置。请前往 appsettings.json 配置 \"Bot:adminId\" 为管理员QQ号。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                else
                {
                    if (!long.TryParse(app.Configuration["Bot:adminId"], out long adminId))
                    {
                        mainLogger.LogError($"管理员QQ号配置无效，无法将 \"{app.Configuration["Bot:adminId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:adminId\" 为正确的管理员QQ号。");
                        Console.WriteLine("\n 按 Enter 退出");
                        Console.ReadLine();
                        return;
                    }
                }
                if (string.IsNullOrEmpty(app.Configuration["Bot:verifyGroupId"]))
                {
                    mainLogger.LogError("审核群组群号未配置。请前往 appsettings.json 配置 \"Bot:verifyGroupId\" 为审核群组群号。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                else
                {
                    if (!long.TryParse(app.Configuration["Bot:verifyGroupId"], out long verifyGroupId))
                    {
                        mainLogger.LogError($"审核群组群号配置无效，无法将 \"{app.Configuration["Bot:verifyGroupId"]}\" 转换为 long .请前往 appsettings.json 配置 \"Bot:verifyGroupId\" 为正确的群号。");
                        Console.WriteLine("\n 按 Enter 退出");
                        Console.ReadLine();
                        return;
                    }
                }
                if (string.IsNullOrWhiteSpace(app.Configuration["Bot:accessToken"]))
                {
                    mainLogger.LogError("OneBot Access Token 未配置。请前往 appsettings.json 添加 Bot:accessToken 配置项。");
                    return;
                }
                if (string.IsNullOrWhiteSpace(app.Configuration["Bot:wsPort"]))
                {
                    mainLogger.LogError("OneBot WebSocket 端口未配置。请前往 appsettings.json 添加 Bot:wsPort 配置项。");
                    return;
                }
                if (!int.TryParse(app.Configuration["Bot:wsPort"], out int wsPort))
                {
                    mainLogger.LogError("OneBot WebSocket 端口配置错误, 无法转型。请前往 appsettings.json 检查 Bot:wsPort 配置项。");
                    return;
                }
                if (string.IsNullOrEmpty(app.Configuration["Survey:packedSurveyPath"]))
                {
                    mainLogger.LogError("问卷题目内容未配置。请前往 appsettings.json 配置 \"Survey:packedSurveyPath\" 为已打包问卷位置。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                if (!File.Exists(app.Configuration["Survey:packedSurveyPath"]))
                {
                    mainLogger.LogError($"问卷题目内容文件 {app.Configuration["Survey:packedSurveyPath"]} 不存在，请检查路径是否正确。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                surveyPkg = SurveyPkgInstance.LoadFromFile(app.Configuration["Survey:packedSurveyPath"]!, surveyLogger);
            }

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
            Timer timer = new Timer(_ =>
            {
                surveyPkg?.Reload();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            app.Run();

            // 设置每分钟自动重新加载问卷

        }

    }
}
