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
                if (string.IsNullOrEmpty(app.Configuration["Bot:mainGroupId"]))
                {
                    mainLogger.LogError("��Ⱥ��Ⱥ��δ���á���ǰ�� appsettings.json ���� \"Bot:mainGroupId\" Ϊ��Ⱥ��Ⱥ�š�");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                else
                {
                    if (!long.TryParse(app.Configuration["Bot:mainGroupId"], out long mainGroupId))
                    {
                        mainLogger.LogError($"��Ⱥ��Ⱥ��������Ч���޷��� \"{app.Configuration["Bot:mainGroupId"]}\" ת��Ϊ long .��ǰ�� appsettings.json ���� \"Bot:mainGroupId\" Ϊ��ȷ��Ⱥ�š�");
                        Console.WriteLine("\n �� Enter �˳�");
                        Console.ReadLine();
                        return;
                    }
                }
                if (string.IsNullOrEmpty(app.Configuration["Bot:adminId"]))
                {
                    mainLogger.LogError("����ԱQQ��δ���á���ǰ�� appsettings.json ���� \"Bot:adminId\" Ϊ����ԱQQ�š�");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                else
                {
                    if (!long.TryParse(app.Configuration["Bot:adminId"], out long adminId))
                    {
                        mainLogger.LogError($"����ԱQQ��������Ч���޷��� \"{app.Configuration["Bot:adminId"]}\" ת��Ϊ long .��ǰ�� appsettings.json ���� \"Bot:adminId\" Ϊ��ȷ�Ĺ���ԱQQ�š�");
                        Console.WriteLine("\n �� Enter �˳�");
                        Console.ReadLine();
                        return;
                    }
                }
                if (string.IsNullOrEmpty(app.Configuration["Bot:verifyGroupId"]))
                {
                    mainLogger.LogError("���Ⱥ��Ⱥ��δ���á���ǰ�� appsettings.json ���� \"Bot:verifyGroupId\" Ϊ���Ⱥ��Ⱥ�š�");
                    Console.WriteLine("\n �� Enter �˳�");
                    Console.ReadLine();
                    return;
                }
                else
                {
                    if (!long.TryParse(app.Configuration["Bot:verifyGroupId"], out long verifyGroupId))
                    {
                        mainLogger.LogError($"���Ⱥ��Ⱥ��������Ч���޷��� \"{app.Configuration["Bot:verifyGroupId"]}\" ת��Ϊ long .��ǰ�� appsettings.json ���� \"Bot:verifyGroupId\" Ϊ��ȷ��Ⱥ�š�");
                        Console.WriteLine("\n �� Enter �˳�");
                        Console.ReadLine();
                        return;
                    }
                }
                if (string.IsNullOrWhiteSpace(app.Configuration["Bot:accessToken"]))
                {
                    mainLogger.LogError("OneBot Access Token δ���á���ǰ�� appsettings.json ��� Bot:accessToken �����");
                    return;
                }
                if (string.IsNullOrWhiteSpace(app.Configuration["Bot:wsPort"]))
                {
                    mainLogger.LogError("OneBot WebSocket �˿�δ���á���ǰ�� appsettings.json ��� Bot:wsPort �����");
                    return;
                }
                if (!int.TryParse(app.Configuration["Bot:wsPort"], out int wsPort))
                {
                    mainLogger.LogError("OneBot WebSocket �˿����ô���, �޷�ת�͡���ǰ�� appsettings.json ��� Bot:wsPort �����");
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
