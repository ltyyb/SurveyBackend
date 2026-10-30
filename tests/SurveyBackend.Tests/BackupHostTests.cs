using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SurveyBackend.Data;
using SurveyBackend.Models;
using SurveyBackend.Services.Backups;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class BackupHostTests
{
    [Fact]
    public async Task Program_SyntheticConfiguration_StartsApiAndCreatesRestorableBackupWithoutOnebot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SurveyBackend.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "data.db");
        using (var source = SqliteDatabase.CreateContext(databasePath))
        {
            SqliteDatabase.Initialize(source);
            source.Users.Add(new User { UserId = "SyntheticAdmin01", QQId = "333333", UserGroup = UserGroup.SuperAdmin });
            source.Questionnaires.Add(new Questionnaire
            {
                QuestionnaireId = "Form0001", SurveyJson = "{\"title\":\"synthetic\"}",
                Survey = new Survey { SurveyId = "Survey01", Title = "synthetic" }
            });
            source.SaveChanges();
        }
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SurveyBackend.dll"));
        foreach (var arg in new[]
        {
            "--contentRoot", directory, "--urls", $"http://127.0.0.1:{FreePort()}",
            "--Database:Path", databasePath, "--Bot:accessToken", "synthetic-token",
            "--Bot:wsPort", FreePort().ToString(), "--Bot:mainGroupId", "111111",
            "--Bot:verifyGroupId", "222222", "--Bot:adminId", "333333",
            "--API:Endpoint", "https://example.test/", "--API:SurveyLinkEndpoint", "https://example.test/survey/",
            "--Backup:Enabled", "true", "--Backup:MinFreeDiskBytes", "0", "--Backup:Oss:Enabled", "false"
        }) start.ArgumentList.Add(arg);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        // 清除宿主配置覆盖，测试只使用上面的合成配置。
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("Backup__", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            var store = new BackupStore(Path.Combine(directory, "backups"), NullLogger<BackupStore>.Instance);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            BackupRecord? record = null;
            while (!deadline.IsCancellationRequested)
            {
                Assert.False(process.HasExited, "Application exited before creating a backup.");
                record = store.ReadAll().FirstOrDefault(item => item.State == "Completed");
                if (record is not null) break;
                await Task.Delay(50, deadline.Token);
            }
            Assert.NotNull(record);
            using var restored = SqliteDatabase.CreateContext(store.GetPath(record.Id, ".db"));
            Assert.Empty(restored.Database.GetPendingMigrations());
            Assert.Contains(restored.Users, user => user.QQId == "333333");
            var address = start.ArgumentList[start.ArgumentList.IndexOf("--urls") + 1];
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("SURVEY-USER-ID", restored.Users.Single().UserId);
            HttpResponseMessage? response = null;
            while (response is null)
            {
                try { response = await client.GetAsync(address + "/api/Survey/Form0001", deadline.Token); }
                catch (HttpRequestException) { await Task.Delay(50, deadline.Token); }
            }
            using var responseScope = response;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            Assert.Equal(0, body.RootElement.GetProperty("status").GetInt32());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var combined = await output + await errors;
            Assert.DoesNotContain("fail:", combined, StringComparison.OrdinalIgnoreCase);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
