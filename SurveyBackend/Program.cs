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



            // ��ʼ������ Load
            using (var scope = app.Services.CreateScope())
            {
                var surveyLogger = scope.ServiceProvider.GetRequiredService<ILogger<SurveyController>>();
                var mainLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                if (string.IsNullOrEmpty(app.Configuration.GetConnectionString("DefaultConnection")))
                {
                    mainLogger.LogError("�����ַ���δ���á���ǰ�� appsettings.json ��� \"DefaultConnection\" �����ַ�����");
                    return;
                }
                if (string.IsNullOrEmpty(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError("˽Կδ���á���ǰ�� appsettings.json ���� \"RSA:privateKeyPath\" Ϊ���˽Կ�洢λ�á�");
                    return;
                }
                if (!File.Exists(app.Configuration["RSA:privateKeyPath"]))
                {
                    mainLogger.LogError($"˽Կ�ļ� {app.Configuration["RSA:privateKeyPath"]} �����ڣ�����·���Ƿ���ȷ��");
                    return;
                }

                // !!!!!!!�� "packed_survey.json" �滻Ϊ���ʵ���ʾ��ļ�·��
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
