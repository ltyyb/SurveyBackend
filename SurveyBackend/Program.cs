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


            // ��ʼ������ Load
            using (var scope = app.Services.CreateScope())
            {
                var surveyLogger = scope.ServiceProvider.GetRequiredService<ILogger<SurveyController>>();
                var mainLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                if (string.IsNullOrEmpty(app.Configuration.GetConnectionString("DefaultConnection")))
                {
                    mainLogger.LogError("�����ַ���δ���á���ǰ�� appsettings.json ��� \"DefaultConnection\" �����ַ�����");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                if (string.IsNullOrEmpty(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError("˽Կδ���á���ǰ�� appsettings.json ���� \"RSA:privateKeyPath\" Ϊ���˽Կ�洢λ�á�");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                if (!File.Exists(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError($"˽Կ�ļ� {app.Configuration["RSA:privateKeyPath"]} �����ڣ�����·���Ƿ���ȷ��");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                if (string.IsNullOrEmpty(app.Configuration["Survey:packedSurveyPath"]))
                {
                    mainLogger.LogError("�ʾ���Ŀ����δ���á���ǰ�� appsettings.json ���� \"Survey:packedSurveyPath\" Ϊ�Ѵ���ʾ�λ�á�");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                if (!File.Exists(app.Configuration["Survey:packedSurveyPath"]))
                {
                    mainLogger.LogError($"�ʾ���Ŀ�����ļ� {app.Configuration["Survey:packedSurveyPath"]} �����ڣ�����·���Ƿ���ȷ��");
                    Console.WriteLine("\n �� Enter �˳�");
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

            // ����ÿ�����Զ����¼����ʾ�

        }
    }
}
