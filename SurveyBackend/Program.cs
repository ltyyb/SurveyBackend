using Microsoft.Extensions.Configuration;
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

            var app = builder.Build();



            // 初始化检查和 Load
            using (var scope = app.Services.CreateScope())
            {
                var surveyLogger = scope.ServiceProvider.GetRequiredService<ILogger<SurveyController>>();
                var mainLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                if (string.IsNullOrEmpty(app.Configuration.GetConnectionString("DefaultConnection")))
                {
                    mainLogger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return;
                }
                if (string.IsNullOrEmpty(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError("私钥未配置。请前往 appsettings.json 配置 \"RSA:privateKeyPath\" 为你的私钥存储位置。");
                    return;
                }
                if (!File.Exists(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError($"私钥文件 {app.Configuration["RSA:privateKeyPath"]} 不存在，请检查路径是否正确。");
                    return;
                }

                // !!!!!!!将 "packed_survey.json" 替换为你的实际问卷文件路径
                Survey.LoadFromFile("packed_survey.json", surveyLogger);
            }

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
