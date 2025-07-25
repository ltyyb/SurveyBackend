using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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

            builder.Services.AddHostedService<OnebotService>();

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
                if (string.IsNullOrEmpty(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError("私钥未配置。请前往 appsettings.json 配置 \"RSA:privateKeyPath\" 为你的私钥存储位置。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
                    return;
                }
                if (!File.Exists(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError($"私钥文件 {app.Configuration["RSA:privateKeyPath"]} 不存在，请检查路径是否正确。");
                    Console.WriteLine("\n 按 Enter 退出");
                    Console.ReadLine();
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
