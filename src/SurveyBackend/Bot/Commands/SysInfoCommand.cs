using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

// sysinfo 指令
public class SysInfoCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "sysinfo";
    public override string[] Aliases => ["sys", "status", "diag"];

    public override string Description => "/survey sysinfo [FULL | DEBUG] 获取后端系统状态。\n  加入 FULL 或 DEBUG 标识可以获取更详细的系统状态信息，但可能需要一定时间收集数据并可能引发GC收集。";

    public override UserGroup[] RequiredPermission => [UserGroup.VerifiedUser, UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public SysInfoCommand(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            // 系统信息
            var Informationalversion = Assembly
                                        .GetExecutingAssembly()
                                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                        .InformationalVersion ?? "未知";
            var memoryUsageMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            var os = $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";
            var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;

            var totalSurveys = await db.Surveys.CountAsync(cancellationToken);
            var totalQuestionnaires = await db.Questionnaires.CountAsync(cancellationToken);
            var totalSubmissions = await db.Submissions.CountAsync(cancellationToken);
            var pendingReviews = await db.ReviewSubmissions.Where(r => r.Status == ReviewStatus.Pending).CountAsync(cancellationToken);

            var msg = $"""
            系统状态
              操作系统: {os}
              运行架构: {RuntimeInformation.ProcessArchitecture}
              机器名称: {Environment.MachineName}

            后端核心状态
              系统时间: {DateTime.Now}
              提交版本: {Informationalversion}
              PID : {Environment.ProcessId}
              托管内存: {memoryUsageMB:F2} MB (近似值)
              运行时长: {(uptime.Days == 0 ? "" : $"{uptime.Days}天")} {uptime.Hours} 小时 {uptime.Minutes} 分钟 {uptime.Seconds} 秒

            问卷状态:
              Surveys 数量: {totalSurveys}
              Questionnaires 数量: {totalQuestionnaires}
              Submissions 数量: {totalSubmissions}
              待审 Submissions 数量: {pendingReviews}
            """;
            return CommandResponse.SuccessResponse(msg);
        }
        else if (args.Length == 1 && args[0].Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var totalSurveys = await db.Surveys.CountAsync(cancellationToken);
            var totalQuestionnaires = await db.Questionnaires.CountAsync(cancellationToken);
            var totalSubmissions = await db.Submissions.CountAsync(cancellationToken);
            var pendingReviews = await db.ReviewSubmissions.Where(r => r.Status == ReviewStatus.Pending).CountAsync(cancellationToken);
            var totalUsers = await db.Users.CountAsync(cancellationToken);
            var totalVerifiedUsers = await db.Users.Where(u => u.UserGroup == UserGroup.VerifiedUser).CountAsync(cancellationToken);
            var totalRequests = await db.Requests.CountAsync(cancellationToken);
            var totalReviewVotes = await db.ReviewVotes.CountAsync(cancellationToken);
            var totalReviewSubmissions = await db.ReviewSubmissions.CountAsync(cancellationToken);

            var Informationalversion = Assembly
                                        .GetExecutingAssembly()
                                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                        .InformationalVersion ?? "未知";
            var memoryUsageMB = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
            var os = $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";
            var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
            var gcInfo = GC.GetGCMemoryInfo();
            ThreadPool.GetAvailableThreads(out int workerThreads, out int ioThreads);
            var process = Process.GetCurrentProcess();
            DateTime lastCheckTime = DateTime.UtcNow;
            TimeSpan lastCpuTime = process.TotalProcessorTime;
            async Task<double> GetCpuUsagePercentAsync()
            {
                var now = DateTime.UtcNow;
                var totalTime = process.TotalProcessorTime;

                var timeDiff = (now - lastCheckTime).TotalMilliseconds;
                var cpuDiff = (totalTime - lastCpuTime).TotalMilliseconds;

                lastCpuTime = totalTime;
                lastCheckTime = now;

                if (timeDiff <= 0) return 0;

                // 计算百分比 (除以环境处理器核心数)
                var cpuUsage = (cpuDiff / timeDiff) / Environment.ProcessorCount * 100.0;

                // 首次调用通常不准，建议调用两次 discard 第一次
                return cpuUsage;
            }
            _ = await GetCpuUsagePercentAsync();
            await Task.Delay(500, cancellationToken); // 等待一段时间以获得更准确的 CPU 使用率
            var cpuUsagePercent = await GetCpuUsagePercentAsync();
            var msg = $"""
            系统状态
              操作系统: {os}
              运行架构: {RuntimeInformation.ProcessArchitecture}
              机器名称: {Environment.MachineName}
              PID : {Environment.ProcessId}
              运行时长: {(uptime.Days == 0 ? "" : $"{uptime.Days}天")} {uptime.Hours} 小时 {uptime.Minutes} 分钟 {uptime.Seconds} 秒
              托管堆内存: {memoryUsageMB:F2} MB
              工作集内存: {Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0:F2} MB
            GC状态
              Gen0 堆大小: {gcInfo.GenerationInfo[0].SizeAfterBytes / 1024.0 / 1024.0:F2} MB
              Gen1 堆大小: {gcInfo.GenerationInfo[1].SizeAfterBytes / 1024.0 / 1024.0:F2} MB
              Gen2 堆大小: {gcInfo.GenerationInfo[2].SizeAfterBytes / 1024.0 / 1024.0:F2} MB
              LOH 堆大小: {gcInfo.GenerationInfo[3].SizeAfterBytes / 1024.0 / 1024.0:F2} MB
              Gen0 Collections: {GC.CollectionCount(0)}
              Gen1 Collections: {GC.CollectionCount(1)}
              Gen2 Collections: {GC.CollectionCount(2)}
            线程池状态
              可用辅助线程: {workerThreads}
              可用异步 I/O 线程: {ioThreads}
              进程总线程数: {Process.GetCurrentProcess().Threads.Count}
            处理器信息
              处理器数量: {Environment.ProcessorCount}
              CPU时间: {Process.GetCurrentProcess().TotalProcessorTime}
              CPU使用率: {cpuUsagePercent:F2}%
            后端核心状态
              系统时间: {DateTime.Now}
              提交版本: {Informationalversion}
            数据统计
              Survey数量: {totalSurveys}
              Questionnaire数量: {totalQuestionnaires}
              Submission数量: {totalSubmissions}
              待审Submission数量: {pendingReviews}
              总已认证用户数量: {totalVerifiedUsers}
              总用户数量: {totalUsers}
              总请求数量: {totalRequests}
              总众审投票数量: {totalReviewVotes}
              总众审提交数量: {totalReviewSubmissions}
            """;
            return CommandResponse.SuccessResponse(msg);
        }
        else
        {
            var msg = """
            参数不正确。
            本命令用于获取系统状态。

            使用方法:
            /survey sysinfo [FULL | DEBUG]
            加入 FULL 或 DEBUG 标识可以获取更详细的系统状态信息。

            例如:
            /survey sysinfo
            /survey sysinfo full
            """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
