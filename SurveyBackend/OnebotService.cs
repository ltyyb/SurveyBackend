using MySqlConnector;
using Sisters.WudiLib;
using Sisters.WudiLib.Posts;
using Sisters.WudiLib.Responses;
using Sisters.WudiLib.WebSocket.Reverse;
using System.Data;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static SurveyBackend.Controllers.SurveyController;
using Message = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend
{
    public class OnebotService : BackgroundService, IOnebotService
    {
        private readonly string _helpText = $"""
                         Survey Service 帮助
            本系统提供问卷调查辅助服务。仅可在六同音游部相关群中使用。
            
            命令指南:
            /survey get <问卷标识符> - 注册用户并获取问卷链接
            /survey vote <Response Id> [a|d] - 投票问卷
            /survey info <Response Id> - 查看问卷回应信息
            /survey insight <Response Id> - 查看问卷 AI 见解
            /survey qq <QQ号> - 查询指定 QQ 用户提交的问卷信息
            /survey disable - 关闭自己的问卷审阅
            
            /survey get <问卷标识符> 指令可以简写为 /survey <问卷标识符>。
            /survey vote 指令后的 Reponse Id 可以简写，具体请参照新问卷提交推送时提示的指令。后跟的 a 为同意，d 为拒绝。
            其余所有指令后的 Reponse Id 参数亦可以简写，具体请参照新问卷提交推送时提示的指令。

            /survey disable 可以关闭自己的问卷审阅，他人将无法再次查看你的问卷。

            
            指令示例:
              /survey entr | 获取入群问卷链接
              /survey vote a782da a | 同意某个问卷回应
              /survey vote a782da d | 拒绝某个问卷回应
              /survey info a782da | 查看某个问卷回应信息

            =======================================================
            你可以在 https://github.com/ltyyb/SurveyBackend 获取后端源码
            Powered by Aunt Studio
            Using ASP.NET Core & Sisters.WudiLib
            -
            Developed by Aunt_nuozhen with ❤
            后端版本: {Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "未知"}
            """;
        private readonly string _adminHelpText = """
                       管理员 | Survey Service 帮助
            指令集: 
                /survey trust <QQ号> - 将指定QQ号添加到数据库并标记 IsVerified = true
                /survey ban <QQ号> - 将指定QQ号添加到数据库并标记 IsVerified = false
                /survey trust - 手动将本群所有用户添加到数据库并标记 IsVerified = true
                /survey re-insight <Response Id> - 重新生成指定问卷的 AI 见解
                /survey delete <Response Id> - 软删除指定问卷响应 (存档后删除主条目)
                /survey restore <Response Id> - 恢复软删除的问卷响应
                /survey hard-delete <Response Id> - 直接在数据库中删除指定问卷响应 (慎用)
                /survey disable <Response Id> - 将响应标记为 Disabled
                /survey list-unreviewed - 列出所有未审核问卷的 Response Id 列表
                /survey disable-service - 暂时禁用问卷服务(软禁止，仅禁止 OneBot 相关，管理员不受限)
                /survey enable-service - 重新启用问卷服务
            """;
        private readonly ILogger<OnebotService> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IConfiguration _configuration;
        private string? connStr;
        private bool isDisabled = false;
        private HttpApiClient? onebotApi = null;

        public DateTime LastMessageTime { get; private set; } = DateTime.Now;

        public OnebotService(ILogger<OnebotService> logger, ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("后台服务已启动");

            connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
            }

            string accessToken = _configuration["Bot:accessToken"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogError("OneBot Access Token 未配置。请前往 appsettings.json 添加 Bot:accessToken 配置项。");
                return;
            }
            if (string.IsNullOrWhiteSpace(_configuration["Bot:wsPort"]))
            {
                _logger.LogError("OneBot WebSocket 端口未配置。请前往 appsettings.json 添加 Bot:wsPort 配置项。");
                return;
            }
            if (!int.TryParse(_configuration["Bot:wsPort"], out int wsPort))
            {
                _logger.LogError("OneBot WebSocket 端口配置错误, 无法转型。请前往 appsettings.json 检查 Bot:wsPort 配置项。");
                return;
            }
            var reverseWSServer = new ReverseWebSocketServer(wsPort);
            reverseWSServer.SetListenerAuthenticationAndConfiguration((listener, selfId) =>
            {
                onebotApi = listener.ApiClient;

                listener.SocketDisconnected += () =>
                {
                    _logger.LogWarning("WebSocket连接已断开");
                    IsAvailable = false;
                };
                listener.EventPosted += (_) =>
                {
                    if (!IsAvailable)
                    {
                        _logger.LogInformation("WebSocket连接已建立, 收到事件。");
                    }
                    IsAvailable = true;
                };
                listener.OnExceptionWithRawContent += (ex, rawContent) =>
                {
                    _logger.LogError(ex, "OneBot 上报发生异常，原始内容: {rawContent}", rawContent);
                };
                listener.MessageEvent += async (api, e) =>
                {
                    LastMessageTime = DateTime.Now;
                    if (e.Content.Text.Trim().ToLowerInvariant().StartsWith("survey"))
                    {
                        _logger.LogInformation("Get survey cmd");
                        await SurveyCmdProcesser(e, stoppingToken);
                    }
                };
                listener.GroupRequestEvent += (api, e) =>
                {
                    _logger.LogInformation("收到群请求: {groupId} 来自 {userId}", e.GroupId, e.UserId);
                    bool isVerified = false;
                    // 验证是否已审核
                    isVerified = IsVerified(e.UserId.ToString());
                    if (isVerified)
                    {
                        return true;
                    }
                    else
                    {
                        return "请先在审核群完成审核。";
                    }
                };
            }, accessToken);
            reverseWSServer.Start(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (IsAvailable)
                {
                    _logger.LogInformation("已连接 OneBot 实现 | OneBot 后台服务运行中：{time}", DateTimeOffset.Now);
                }
                else
                {
                    _logger.LogInformation("未连接 OneBot 实现 | OneBot 后台服务运行中：{time}", DateTimeOffset.Now);
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // 每1分钟执行一次
            }

            _logger.LogInformation("后台服务已停止");
        }
        private async Task SurveyCmdProcesser(Message e, CancellationToken cancellationToken)
        {
            try
            {
                if (_configuration["IsDisabled"] == "true")
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, "问卷服务当前不可用。后端服务可能正在维护。如有疑问请联系管理员。");
                    return;
                }
                if (isDisabled)
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, "问卷服务当前不可用。后端服务可能正在维护。如有疑问请联系管理员。");
                    if (isAdmin(e.UserId))
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "哦！您是高贵的管理！马上办~");
                    }
                    else return;
                }
                // 将信息以空格分割
                var args = e.Content.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length < 2)
                {
                    // 发送帮助信息
                    await SendMessage(e.Endpoint, _helpText);
                    if (isAdmin(e.UserId)) await SendMessageWithAt(e.Endpoint, e.UserId, _adminHelpText);
                    return;
                }
                else if (args.Length == 2)
                {
                    if (args[1] == "trust")
                    {
                        if (e is GroupMessage groupMsg)
                        {
                            if (groupMsg.Sender.UserId.ToString() == _configuration["Bot:adminId"]
                                && groupMsg.GroupId.ToString() == _configuration["Bot:mainGroupId"])
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "将强制为本群所有用户注册到数据库并自动配置 IsVerified = true..");
                                await TrustGroup(groupMsg.GroupId, cancellationToken);
                            }
                            else
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                                return;
                            }
                        }
                    }
                    else if (args[1] == "disable-service")
                    {
                        if (isAdmin(e.UserId))
                        {
                            isDisabled = true;
                            await SendMessageWithAt(e.Endpoint, e.UserId, "问卷服务已被软禁用。管理员不受限制。\n要获得进一步的限制，请更改 appsettings.json");
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                        return;
                    }
                    else if (args[1] == "enable-service")
                    {
                        if (isAdmin(e.UserId))
                        {
                            isDisabled = false;
                            await SendMessageWithAt(e.Endpoint, e.UserId, "问卷服务已被启用。");
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                        return;
                    }
                    else if (args[1] == "disable")
                    {
                        var responseId = await ResponseTools.GetResponseIdOfQQId(e.UserId.ToString(), _logger, connStr!);
                        if (string.IsNullOrWhiteSpace(responseId))
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "找不到你的问卷回应记录。你可能还没有提交过问卷，或者你的问卷回应已被删除。");
                            return;
                        }
                        bool result = await ResponseTools.DisableResponse(responseId, _logger, connStr!);
                        if (result)
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "你的问卷回应已被标记为不可审阅。其他人将无法查看你的问卷内容。如果你想重新启用审阅，请联系管理员。");
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "无法标记你的问卷回应为不可审阅。请稍后再试，或联系管理员。");
                        }
                    }
                    else if (args[1] == "list-unreviewed")
                    {
                        if (isAdmin(e.UserId))
                        {
                            var responses = await ResponseTools.GetUnreviewedResponseList(_logger, connStr!);
                            if (responses is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "获取失败");
                                return;
                            }
                            if (responses.Count == 0)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "当前没有未审阅的问卷回应。");
                                return;
                            }
                            var sb = new StringBuilder();
                            sb.AppendLine($"当前共有 {responses.Count} 个未审阅的问卷回应:");
                            foreach (var resp in responses)
                            {
                                sb.AppendLine($"""
                                      - {resp.responseId}
                                       | 提交者: {resp.qqId}

                                    """);
                            }
                            await SendMessageWithAt(e.Endpoint, e.UserId, sb.ToString());
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else
                    {
                        var surveyId = args[1];
                        bool flowControl = await ProcessGetCommand(e, surveyId);
                        if (!flowControl)
                        {
                            return;
                        }
                    }


                }
                else if (args.Length == 3)
                {
                    if (args[1] == "get")
                    {
                        var surveyId = args[2];
                        bool flowControl = await ProcessGetCommand(e, surveyId);
                        if (!flowControl)
                        {
                            return;
                        }
                    }
                    else if (args[1] == "info")
                    {
                        if (e is GroupMessage groupMsg)
                        {
                            if (groupMsg.GroupId.ToString() != _configuration["Bot:mainGroupId"])
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限在此群聊中使用这一指令。");
                                return;
                            }
                        }
                        var responseId = args[2];
                        if (responseId.Length < 10)
                        {
                            var fullId = await GetFullResponseIdAsync(responseId);
                            if (string.IsNullOrWhiteSpace(fullId))
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"[信息查询] 无法补全 ResponseId。请检查 ShortId 是否正确。");
                                return;
                            }
                            responseId = fullId;
                        }

                        var link = $"https://ltyyb.auntstudio.com/survey/entr/review?surveyId={responseId}";

                        await using var conn = new MySqlConnection(connStr);
                        await conn.OpenAsync(cancellationToken);
                        const string infoQuery = "SELECT * FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                        await using var infoCmd = new MySqlCommand(infoQuery, conn);
                        infoCmd.Parameters.AddWithValue("@responseId", responseId);
                        await using var reader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            // 读取信息
                            var responseInfo = $"""
                            [基本提交信息] {responseId[..8]}
                            提交者: {reader.GetString("QQId")}
                            问卷版本: {reader.GetString("SurveyVersion")}
                            已审阅: {(reader.GetBoolean("IsReviewed") ? "是" : "否")}
                            提交时间: {reader.GetDateTime("CreatedAt"):yyyy-MM-dd HH:mm:ss}

                            审阅链接: {link}
                            """;
                            await SendMessageWithAt(e.Endpoint, e.UserId, responseInfo);
                        }

                        await using var voteConn = new MySqlConnection(connStr);
                        await voteConn.OpenAsync(cancellationToken);
                        const string voteQuery = "SELECT SUM(vote = 'agree') AS agreeCount, SUM(vote = 'deny') AS denyCount FROM response_votes WHERE responseId = @responseId";
                        await using var voteCmd = new MySqlCommand(voteQuery, voteConn);
                        voteCmd.Parameters.AddWithValue("@responseId", responseId);
                        await using var voteReader = await voteCmd.ExecuteReaderAsync(cancellationToken);

                        if (await voteReader.ReadAsync(cancellationToken))
                        {
                            // 读取信息
                            int agreeCount = voteReader.GetInt32("agreeCount"), denyCount = voteReader.GetInt32("denyCount");
                            var voteInfo = $"""
                            [投票信息] {responseId[..8]}
                            赞成票数: {agreeCount}
                            反对票数: {denyCount}

                            赞成比例: {((agreeCount + denyCount) != 0 ? (agreeCount / (agreeCount + denyCount)) : "无投票"):F2}
                            """;

                            await SendMessageWithAt(e.Endpoint, e.UserId, voteInfo);
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"[信息查询] 未找到指定的 Response Id \"{responseId}\"。");
                        }
                    }
                    else if (args[1] == "info-raw")
                    {
                        if (e is GroupMessage groupMsg)
                        {
                            if (groupMsg.GroupId.ToString() != _configuration["Bot:mainGroupId"])
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限在此群聊中使用这一指令。");
                                return;
                            }
                        }
                        string responseId = args[2];
                        if (responseId.Length < 10)
                        {
                            var fullId = await GetFullResponseIdAsync(responseId);
                            if (string.IsNullOrWhiteSpace(fullId))
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"[Raw 信息查询] 无法补全 ResponseId。请检查 ShortId 是否正确。");
                                return;
                            }
                            responseId = fullId;
                        }

                        await using var conn = new MySqlConnection(connStr);
                        await conn.OpenAsync(cancellationToken);
                        const string infoQuery = "SELECT * FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                        await using var infoCmd = new MySqlCommand(infoQuery, conn);
                        infoCmd.Parameters.AddWithValue("@responseId", responseId);
                        await using var reader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            // 读取信息
                            var responseInfo = new StringBuilder();
                            responseInfo.AppendLine($"ResponseId: {reader.GetString("ResponseId")}");
                            responseInfo.AppendLine($"UserId: {reader.GetString("UserId")}");
                            responseInfo.AppendLine($"QQId: {reader.GetString("QQId")}");
                            responseInfo.AppendLine($"ShortId: {reader.GetString("ShortId")}");
                            responseInfo.AppendLine($"SurveyVersion: {reader.GetString("SurveyVersion")}");
                            responseInfo.AppendLine($"IsPushed: {reader.GetBoolean("IsPushed")}");
                            responseInfo.AppendLine($"IsReviewed: {reader.GetBoolean("IsReviewed")}");
                            responseInfo.AppendLine($"CreatedAt: {reader.GetDateTime("CreatedAt"):yyyy-MM-dd HH:mm:ss}");
                            await SendMessageWithAt(e.Endpoint, e.UserId, "[基本提交信息 RAW]\n" + responseInfo.ToString());
                        }
                        await using var voteConn = new MySqlConnection(connStr);
                        await voteConn.OpenAsync(cancellationToken);
                        const string voteQuery = "SELECT SUM(vote = 'agree') AS agreeCount, SUM(vote = 'deny') AS denyCount FROM response_votes WHERE responseId = @responseId";
                        await using var voteCmd = new MySqlCommand(voteQuery, voteConn);
                        voteCmd.Parameters.AddWithValue("@responseId", responseId);
                        await using var voteReader = await voteCmd.ExecuteReaderAsync(cancellationToken);

                        if (await voteReader.ReadAsync(cancellationToken))
                        {
                            // 读取信息
                            int agreeCount = voteReader.GetInt32("agreeCount"), denyCount = voteReader.GetInt32("denyCount");
                            var voteInfo = $"""
                            [投票信息] {responseId[..8]}
                            赞成票数: {agreeCount}
                            反对票数: {denyCount}

                            赞成比例: {((agreeCount + denyCount) != 0 ? (agreeCount / (agreeCount + denyCount)) : "无投票"):F2}
                            反对比例: {((agreeCount + denyCount) != 0 ? (denyCount / (agreeCount + denyCount)) : "无投票"):F2}
                            """;

                            await SendMessageWithAt(e.Endpoint, e.UserId, voteInfo);

                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"[Raw 信息查询] 未找到指定的 Response Id \"{responseId}\"。");
                        }
                    }
                    else if (args[1] == "vote")
                    {
                        // 发送帮助信息
                        var errorMsg = new SendingMessage("投票指令格式错误。请使用 /survey vote <Response Id> [a|d]。\n================\n\n");
                        await SendMessageWithAt(e.Endpoint, e.UserId, errorMsg + _helpText);
                        return;
                    }
                    else if (args[1] == "trust")
                    {
                        if (isAdmin(e.UserId))
                        {
                            if (!long.TryParse(args[2], out long qqId))
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法将指定的用户添加到数据库中。请提供有效的 QQ 号。");
                            }
                            if (await TrustUser(qqId, cancellationToken))
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"用户 {qqId} 已成功添加到数据库中并受信。");
                            }
                            else
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"无法将用户 {qqId} 添加到数据库中。");
                            }
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else if (args[1] == "ban")
                    {
                        if (isAdmin(e.UserId))
                        {
                            if (!long.TryParse(args[2], out long qqId))
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法将指定的用户添加到数据库中。请提供有效的 QQ 号。");
                            }
                            if (await BanUser(qqId, cancellationToken))
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"用户 {qqId} 已成功添加到数据库中并不再受信。");
                            }
                            else
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"无法将用户 {qqId} 添加到数据库中。");
                            }
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else if (args[1] == "insight")
                    {
                        if (e is GroupMessage groupMsg)
                        {
                            if (groupMsg.GroupId.ToString() != _configuration["Bot:mainGroupId"])
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限在此群聊中使用这一指令。");
                                return;
                            }
                            var responseId = await GetFullResponseIdAsync(args[2]);
                            if (responseId is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法补全 ResponseId, 请检查 ShortId 是否正确。");
                                return;
                            }

                            await using var conn = new MySqlConnection(connStr);
                            await conn.OpenAsync(cancellationToken);
                            const string infoQuery = "SELECT LLMInsight FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                            await using var infoCmd = new MySqlCommand(infoQuery, conn);
                            infoCmd.Parameters.AddWithValue("@responseId", responseId);
                            await using var reader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                // 读取信息
                                var insight = reader.IsDBNull("LLMInsight") ? "无" : reader.GetString("LLMInsight");
                                var msg = $"""
                                {responseId}
                                AI 见解
                                以下内容由 AI 生成, 仅供参考:
                                =================================
                                {insight}
                                """;
                                await SendMessageWithAt(e.Endpoint, e.UserId, msg);
                            }
                        }

                    }
                    else if (args[1] == "re-insight")
                    {
                        if (isAdmin(e.UserId))
                        {
                            var responseId = await GetFullResponseIdAsync(args[2]);
                            if (responseId is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法补全 ResponseId, 请检查 ShortId 是否正确。");
                                return;
                            }
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"即将重新生成 AI 见解并覆盖先前结果: {responseId}");
                            await ReGenerateInsight(responseId, e);
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"已经重新生成 AI 见解并覆盖先前结果: {responseId}");

                            await using var conn = new MySqlConnection(connStr);
                            await conn.OpenAsync(cancellationToken);
                            const string infoQuery = "SELECT LLMInsight FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                            await using var infoCmd = new MySqlCommand(infoQuery, conn);
                            infoCmd.Parameters.AddWithValue("@responseId", responseId);
                            await using var reader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                // 读取信息
                                var insight = reader.IsDBNull("LLMInsight") ? "无" : reader.GetString("LLMInsight");
                                var msg = $"""
                                {responseId}
                                AI 见解
                                以下内容由 AI 生成, 仅供参考:
                                =================================
                                {insight}
                                """;
                                await SendMessageWithAt(e.Endpoint, e.UserId, msg);
                            }
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else if (args[1] == "delete")
                    {
                        if (isAdmin(e.UserId))
                        {
                            var responseId = await GetFullResponseIdAsync(args[2]);
                            if (responseId is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法补全 ResponseId, 请检查 ShortId 是否正确。");
                                return;
                            }
                            bool result = await ResponseTools.SoftDeleteResponse(responseId, _logger, connStr!);
                            if (result)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"已软删除 ResponseId {responseId}。");
                            }
                            else
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"无法软删除 ResponseId {responseId}。");
                            }
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else if (args[1] == "restore")
                    {
                        if (isAdmin(e.UserId))
                        {
                            var responseId = await GetDeletedFullResponseIdAsync(args[2]);
                            if (responseId is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法补全 ResponseId, 请检查 ShortId 是否正确。");
                                return;
                            }
                            bool result = await ResponseTools.RestoreResponse(responseId, _logger, connStr!);
                            if (result)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"已恢复 ResponseId {responseId}。");
                            }
                            else
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"无法恢复 ResponseId {responseId}。");
                            }

                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else if (args[1] == "hard-delete")
                    {
                        if (isAdmin(e.UserId))
                        {
                            var responseId = await GetFullResponseIdAsync(args[2]);
                            if (responseId is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法补全 ResponseId, 请检查 ShortId 是否正确。");
                                return;
                            }
                            bool result = await ResponseTools.HardDeleteResponse(responseId, _logger, connStr!);
                            if (result)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"已硬删除 ResponseId {responseId}。");
                            }
                            else
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"无法硬删除 ResponseId {responseId}。");
                            }

                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else if (args[1] == "qq")
                    {
                        var responseId = await ResponseTools.GetResponseIdOfQQId(args[2], _logger, connStr!);
                        if (string.IsNullOrWhiteSpace(responseId))
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"找不到用户 {args[2]} 的问卷回应记录。该用户可能还没有提交过问卷，或者该用户的问卷回应已被删除。");
                            return;
                        }
                        var link = $"https://ltyyb.auntstudio.com/survey/entr/review?surveyId={responseId}";

                        await using var conn = new MySqlConnection(connStr);
                        await conn.OpenAsync(cancellationToken);
                        const string infoQuery = "SELECT * FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                        await using var infoCmd = new MySqlCommand(infoQuery, conn);
                        infoCmd.Parameters.AddWithValue("@responseId", responseId);
                        await using var reader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            // 读取信息
                            var responseInfo = $"""
                            [基本提交信息] {responseId[..8]}
                            提交者: {reader.GetString("QQId")}
                            问卷版本: {reader.GetString("SurveyVersion")}
                            已审阅: {(reader.GetBoolean("IsReviewed") ? "是" : "否")}
                            提交时间: {reader.GetDateTime("CreatedAt"):yyyy-MM-dd HH:mm:ss}

                            审阅链接: {link}
                            """;
                            await SendMessageWithAt(e.Endpoint, e.UserId, responseInfo);
                        }

                        await using var voteConn = new MySqlConnection(connStr);
                        await voteConn.OpenAsync(cancellationToken);
                        const string voteQuery = "SELECT SUM(vote = 'agree') AS agreeCount, SUM(vote = 'deny') AS denyCount FROM response_votes WHERE responseId = @responseId";
                        await using var voteCmd = new MySqlCommand(voteQuery, voteConn);
                        voteCmd.Parameters.AddWithValue("@responseId", responseId);
                        await using var voteReader = await voteCmd.ExecuteReaderAsync(cancellationToken);

                        if (await voteReader.ReadAsync(cancellationToken))
                        {
                            // 读取信息
                            int agreeCount = voteReader.GetInt32("agreeCount"), denyCount = voteReader.GetInt32("denyCount");
                            var voteInfo = $"""
                            [投票信息] {responseId[..8]}
                            赞成票数: {agreeCount}
                            反对票数: {denyCount}

                            赞成比例: {((agreeCount + denyCount) != 0 ? (agreeCount / (agreeCount + denyCount)) : "无投票"):F2}
                            """;

                            await SendMessageWithAt(e.Endpoint, e.UserId, voteInfo);
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"[QQ信息查询] 未找到指定的 Response Id \"{responseId}\"。");
                        }

                    }
                    else if (args[1] == "disable")
                    {
                        if (isAdmin(e.UserId))
                        {
                            var responseId = await GetFullResponseIdAsync(args[2]);
                            if (responseId is null)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法补全 ResponseId, 请检查 ShortId 是否正确。");
                                return;
                            }
                            bool result = await ResponseTools.DisableResponse(responseId, _logger, connStr!);
                            if (!result)
                            {
                                await SendMessageWithAt(e.Endpoint, e.UserId, $"无法标记 ResponseId {responseId} 为 Disabled。");
                                return;
                            }
                            await SendMessageWithAt(e.Endpoint, e.UserId, $"已将 ResponseId {responseId} 标记为 Disabled。");
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限使用这一指令。");
                            return;
                        }
                    }
                    else
                    {
                        // 发送帮助信息
                        var errorMsg = new SendingMessage("未知的指令。\n================\n\n");
                        await SendMessageWithAt(e.Endpoint, e.UserId, errorMsg + _helpText);
                        return;
                    }
                }
                else if (args.Length == 4)
                {
                    if (args[1] == "vote")
                    {
                        var responseId = args[2];
                        var vote = args[3].ToLowerInvariant();
                        if (vote != "a" && vote != "d")
                        {
                            // 发送帮助信息
                            var errorMsg = new SendingMessage("投票指令格式错误。请使用 /survey vote <Response Id> [a|d]。\n================\n\n");
                            await SendMessageWithAt(e.Endpoint, e.UserId, errorMsg + _helpText);
                            return;
                        }

                        bool succ = await ProcessVoteCommand(e, responseId, vote == "a", cancellationToken);
                        if (succ)
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "[投票处理] 投票已处理。");
                        }
                        else
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "[投票处理] 投票处理失败。可能未正常记录结果，请告知管理员。");
                        }
                    }
                    else
                    {
                        // 发送帮助信息
                        var errorMsg = new SendingMessage("未知的指令。\n================\n\n");
                        await SendMessageWithAt(e.Endpoint, e.UserId, errorMsg + _helpText);
                        return;
                    }
                }
                else
                {
                    // 发送帮助信息
                    var errorMsg = new SendingMessage("未知的指令。\n================\n\n");
                    await SendMessageWithAt(e.Endpoint, e.UserId, errorMsg + _helpText);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 /survey 指令时发生异常。");
            }

        }

        private async Task<bool> ProcessGetCommand(Message e, string surveyId)
        {
            if (e is GroupMessage groupMsg)
            {
                if (groupMsg.GroupId.ToString() != _configuration["Bot:verifyGroupId"])
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, "请在审核群中使用此命令。");
                    return false;
                }
            }
            if (string.IsNullOrWhiteSpace(surveyId))
            {
                // 发送帮助信息
                await SendMessage(e.Endpoint, _helpText);
                return false;
            }

            if (surveyId == "entr")
            {
                var qqId = e.UserId;
                if (await IsVerifiedAsync(qqId.ToString()))
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, $"您已通过审核。您可直接加入群组 {_configuration["Bot:mainGroupId"]}。");
                    return false;
                }
                if (await IsUserSubmitted(qqId.ToString()))
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, $"数据库中已有您的提交信息。\n请耐心等待审核，如有疑问请联系管理员。");
                    return false;
                }
                await SendMessage(e.Endpoint, "正在注册用户...");

                SurveyUser? surveyUser;

                if (await SurveyUser.IsUserExisted(e.UserId.ToString(), connStr!))
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, "用户已存在。跳过注册。");
                    surveyUser = await SurveyUser.GetUserByQQIdAsync(qqId.ToString(), _logger, connStr!);
                    if (surveyUser is null)
                    {
                        _logger.LogWarning("Cannot get user info from qqId {qqId}", qqId.ToString());
                        await SendMessageWithAt(e.Endpoint, e.UserId, "无法获取已有用户信息。请联系管理员。");
                        return false;
                    }
                }
                else
                {
                    surveyUser = await SurveyUser.CreateUserByQQId(qqId.ToString(), connStr!);

                    if (surveyUser is null)
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "注册用户失败，请联系管理员。\n surveyUser is null.");
                        return false;
                    }


                    else if (!await surveyUser.RegisterAsync(_logger, connStr!))
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "注册用户失败，请联系管理员。\n Cannot register user.");
                        return false;
                    }


                }


                var requestId = await surveyUser.GetTempRequestId(_logger, connStr!);

                if (string.IsNullOrWhiteSpace(requestId))
                {
                    await SendMessageWithAt(e.Endpoint, e.UserId, "无法获取 RequestId，请联系管理员。\n RequestId is null or empty.");
                    return false;
                }

                var link = $"https://ltyyb.auntstudio.com/survey/entr?requestId={requestId}";
                var atMessage = SendingMessage.At(e.UserId);
                var message = new SendingMessage($"""

                        已成功注册 Survey 用户。
                        请访问链接下方链接:
                        
                        {link}

                        完成问卷。
                        如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。
                        请注意，此链接2小时内有效。
                        如果您在1小时以内获取过问卷，则该链接继承上一链接有效期。

                        请注意看清链接所属用户，请勿填写他人问卷链接。
                        本消息对应用户: 
                        """);
                await SendMessage(e.Endpoint, atMessage + message + atMessage);
            }
            else
            {
                // 发送帮助信息
                var errorMsg = new SendingMessage("未知的 Survey Id。\n================\n\n");
                await SendMessage(e.Endpoint, errorMsg + _helpText);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 处理投票指令。
        /// </summary>
        /// <param name="e"></param>
        /// <param name="responseId">完整或短 ReponseId</param>
        /// <param name="isAgree"></param>
        /// <returns></returns>
        private async Task<bool> ProcessVoteCommand(Message e, string responseId, bool isAgree, CancellationToken cancellationToken)
        {
            try
            {
                if (e is GroupMessage groupMsg)
                {
                    if (groupMsg.GroupId.ToString() != _configuration["Bot:mainGroupId"])
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "您没有权限在此群聊中使用这一指令。");
                        return false;
                    }
                }

                string? fullId;
                if (responseId.Length > 10)
                {// Full ResponseId
                    fullId = responseId;
                }
                else
                {// ShortId
                    fullId = await GetFullResponseIdAsync(responseId);
                    if (string.IsNullOrWhiteSpace(fullId))
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, $"无法获取 {responseId} 的完整 ResponseId, 请检查输入是否正确。");
                        return false;
                    }

                }

                var (succ, isReviewed) = await IsResponseReviewed(fullId, cancellationToken);
                if (succ && isReviewed is not null)
                {
                    if ((bool)isReviewed)
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, $"[投票处理] ResponseId {fullId} 已经被审核，无法进行投票。");
                        return false;
                    }
                }

                // 注册用户
                var qqId = e.UserId;
                _logger.LogInformation("正在将用户 {user} 注册到数据库中以便投票数据写入。", qqId);

                SurveyUser? surveyUser;

                if (await SurveyUser.IsUserExisted(e.UserId.ToString(), connStr!))
                {
                    _logger.LogInformation("用户 {user} 已存在于数据库。跳过注册", qqId);
                    surveyUser = await SurveyUser.GetUserByQQIdAsync(qqId.ToString(), _logger, connStr!);
                    if (surveyUser is null)
                    {
                        _logger.LogWarning("Cannot get user info from qqId {qqId}", qqId.ToString());
                        await SendMessageWithAt(e.Endpoint, e.UserId, "[投票处理] 无法获取已有用户信息。请联系管理员。");
                        return false;
                    }
                }
                else
                {
                    surveyUser = await SurveyUser.CreateUserByQQId(qqId.ToString(), connStr!);

                    if (surveyUser is null)
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "[投票处理] 注册用户失败，请联系管理员。\n surveyUser is null.");
                        return false;
                    }


                    else if (!await surveyUser.RegisterAsync(_logger, connStr!))
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "[投票处理] 注册用户失败，请联系管理员。\n Cannot register user.");
                        return false;
                    }
                }

                _logger.LogInformation("用户 {user} 成功，获得 UserId: {userId}", qqId, surveyUser.UserId);
                return await VoteAsync(e, surveyUser.UserId, fullId, isAgree);
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "处理投票指令时发生错误");
                return false;
            }
        }

        private async Task<bool> VoteAsync(Message e, string userId, string responseId, bool isAgree)
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            const string fullInsertSql = "INSERT INTO response_votes (responseId, userId, vote) VALUES (@responseId, @userId, @vote) ON DUPLICATE KEY UPDATE vote = VALUES(vote), voteTime = CURRENT_TIMESTAMP";
            await using var cmd = new MySqlCommand(fullInsertSql, conn);
            cmd.Parameters.AddWithValue("@responseId", responseId);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@vote", isAgree ? "agree" : "deny");
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                _logger.LogInformation("投票成功: {responseId} by {userId}", responseId, userId);
                var voteResult = isAgree ? "同意" : "拒绝";
                await SendMessageWithAt(e.Endpoint, e.UserId, $"[投票处理] 投票成功:\n {voteResult} => {responseId}");
                return true;
            }
            else
            {
                _logger.LogWarning("投票失败: {responseId} by {userId}", responseId, userId);
                await SendMessageWithAt(e.Endpoint, e.UserId, $"[投票处理] 投票失败，请稍后再试。\n rowsAffected == {rowsAffected}");
                return false;
            }
        }

        private async Task<string?> GetFullResponseIdAsync(string shortId)
        {
            return await ResponseTools.GetFullResponseIdAsync(shortId, _logger, connStr!);
        }

        private async Task<string?> GetDeletedFullResponseIdAsync(string shortId)
        {
            return await ResponseTools.GetFullResponseIdAsync(shortId, _logger, connStr!, true);
        }

        private bool IsVerified(string qqId)
        {
            try
            {
                using var conn = new MySqlConnection(connStr);
                conn.Open();
                const string query = "SELECT IsVerified FROM qqusers WHERE QQId = @qqId";
                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@qqId", qqId);
                var result = cmd.ExecuteScalar() ?? false;
                _logger.LogInformation(qqId + " IsVerified: " + result);
                return Convert.ToBoolean(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户是否已验证时发生错误");
                return false;
            }
        }
        private async Task<bool> IsVerifiedAsync(string qqId)
        {
            try
            {
                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                const string query = "SELECT IsVerified FROM qqusers WHERE QQId = @qqId";
                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@qqId", qqId);
                var result = await cmd.ExecuteScalarAsync() ?? false;
                _logger.LogInformation(qqId + " IsVerified: " + result);
                return Convert.ToBoolean(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户是否已验证时发生错误");
                return false;
            }
        }

        private async Task<(bool succ, bool? isReviewed)> IsResponseReviewed(string responseId, CancellationToken cancellationToken)
        {
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync(cancellationToken);
                const string query = "SELECT IsReviewed FROM EntranceSurveyResponses WHERE ResponseId = @responseId";
                await using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);
                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                if (result != null && result != DBNull.Value)
                {
                    return (true, Convert.ToBoolean(result));
                }
                else
                {
                    _logger.LogError("Response {responseId} 未找到", responseId);
                    return (false, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查 Response 是否已审核时发生错误");
                return (false, null);
            }
        }

        private async Task<bool> TrustGroup(long groupId, CancellationToken cancellationToken)
        {
            try
            {
                if (onebotApi is null)
                {
                    _logger.LogError("Onebot API 客户端未初始化");
                    return false;
                }
                bool succ = true;
                var members = await onebotApi.GetGroupMemberListAsync(groupId);
                foreach (var member in members)
                {

                    if (!await TrustUser(member.UserId, cancellationToken) || !succ)
                    {
                        succ = false;
                    }
                }
                return succ;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "尝试将群 {groupId} 中的用户注册到数据库中时发生错误", groupId);
                return false;
            }
        }

        private async Task<bool> TrustUser(long userQQId, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("正在将用户 {user} 注册到数据库中。", userQQId.ToString());

                SurveyUser? surveyUser;

                if (await SurveyUser.IsUserExisted(userQQId.ToString(), connStr!))
                {
                    _logger.LogInformation("用户 {user} 已存在于数据库。跳过注册", userQQId.ToString());
                    surveyUser = await SurveyUser.GetUserByQQIdAsync(userQQId.ToString(), _logger, connStr!);
                    if (surveyUser is null)
                    {
                        _logger.LogWarning("Cannot get user info from qqId {qqId}", userQQId.ToString());
                        return false;
                    }
                }
                else
                {
                    surveyUser = await SurveyUser.CreateUserByQQId(userQQId.ToString(), connStr!);

                    if (surveyUser is null)
                    {
                        _logger.LogWarning("Cannot create user info from qqId {qqId}", userQQId.ToString());
                        return false;
                    }


                    else if (!await surveyUser.RegisterAsync(_logger, connStr!))
                    {
                        _logger.LogWarning("Cannot register user info from qqId {qqId}", userQQId.ToString());
                        return false;
                    }
                }

                _logger.LogInformation("用户 {user} 成功，获得 UserId: {userId}", userQQId.ToString(), surveyUser.UserId);


                await using var userConnection = new MySqlConnection(connStr);
                await userConnection.OpenAsync(cancellationToken);
                const string updateUserQuery = "UPDATE QQUsers SET IsVerified = true WHERE UserId = @userId";
                await using var updateUserCmd = new MySqlCommand(updateUserQuery, userConnection);
                updateUserCmd.Parameters.AddWithValue("@userId", surveyUser.UserId);
                var userAffected = await updateUserCmd.ExecuteNonQueryAsync(cancellationToken);
                if (userAffected == 0)
                {
                    _logger.LogWarning("无法将 {qqId} 添加到数据库中。", userQQId.ToString());
                }
                else
                {
                    _logger.LogWarning("成功将 {qqId} 添加到数据库中并受信。", userQQId.ToString());
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "尝试添加用户 {qqId} 时发生错误", userQQId.ToString());
                return false;
            }
        }
        private async Task<bool> BanUser(long userQQId, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("正在将用户 {user} 注册到数据库中.", userQQId.ToString());

                SurveyUser? surveyUser;

                if (await SurveyUser.IsUserExisted(userQQId.ToString(), connStr!))
                {
                    _logger.LogInformation("用户 {user} 已存在于数据库。跳过注册", userQQId.ToString());
                    surveyUser = await SurveyUser.GetUserByQQIdAsync(userQQId.ToString(), _logger, connStr!);
                    if (surveyUser is null)
                    {
                        _logger.LogWarning("Cannot get user info from qqId {qqId}", userQQId.ToString());
                        return false;
                    }
                }
                else
                {
                    surveyUser = await SurveyUser.CreateUserByQQId(userQQId.ToString(), connStr!);

                    if (surveyUser is null)
                    {
                        _logger.LogWarning("Cannot create user info from qqId {qqId}", userQQId.ToString());
                        return false;
                    }


                    else if (!await surveyUser.RegisterAsync(_logger, connStr!))
                    {
                        _logger.LogWarning("Cannot register user info from qqId {qqId}", userQQId.ToString());
                        return false;
                    }
                }

                _logger.LogInformation("用户 {user} 成功，获得 UserId: {userId}", userQQId.ToString(), surveyUser.UserId);


                await using var userConnection = new MySqlConnection(connStr);
                await userConnection.OpenAsync(cancellationToken);
                const string updateUserQuery = "UPDATE QQUsers SET IsVerified = false WHERE UserId = @userId";
                await using var updateUserCmd = new MySqlCommand(updateUserQuery, userConnection);
                updateUserCmd.Parameters.AddWithValue("@userId", surveyUser.UserId);
                var userAffected = await updateUserCmd.ExecuteNonQueryAsync(cancellationToken);
                if (userAffected == 0)
                {
                    _logger.LogWarning("无法将 {qqId} 添加到数据库中。", userQQId.ToString());
                }
                else
                {
                    _logger.LogWarning("成功将 {qqId} 添加到数据库中并不再受信。", userQQId.ToString());
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "尝试添加用户 {qqId} 时发生错误", userQQId.ToString());
                return false;
            }
        }

        private async Task<bool> IsUserSubmitted(string qqId)
        {
            try
            {
                return await CheckValueExistsAsync(connStr!, "EntranceSurveyResponses", "QQId", qqId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户是否已提交时发生错误");
                return false;
            }
        }
        private static async Task<bool> CheckValueExistsAsync(string connectionString, string tableName, string columnName, object value)
        {
            Regex SafeNameRegex = new(@"^[a-zA-Z0-9_]+$");
            // 防止 SQL 注入：只允许合法字符
            if (!SafeNameRegex.IsMatch(tableName) || !SafeNameRegex.IsMatch(columnName))
            {
                throw new ArgumentException("表名或列名包含非法字符。只允许字母、数字和下划线。");
            }

            string query = $"SELECT EXISTS(SELECT 1 FROM `{tableName}` WHERE `{columnName}` = @value LIMIT 1);";

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@value", value);

            object result = await command.ExecuteScalarAsync() ?? false;

            return Convert.ToBoolean(result);
        }
        private async Task<SurveyResponse?> GetResponseByResponseId(string responseId)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return null;
                }
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                const string sql = "SELECT SurveyAnswer, SurveyVersion, UserId, QQId FROM EntranceSurveyResponses WHERE ResponseId = @responseId LIMIT 1";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())  // 只读取第一行
                {
                    string response = reader.GetString("SurveyAnswer");
                    string version = reader.GetString("SurveyVersion");
                    string userId = reader.GetString("userId");
                    string qqId = reader.GetString("QQId");
                    var responseData = new SurveyResponse
                    {
                        UserId = userId,
                        SurveyVersion = version,
                        QQId = qqId,
                        SurveyAnswer = response
                    };
                    return responseData;
                }
                else
                {
                    _logger.LogWarning("Could not find survey response with id {id}", responseId);
                    return null;
                }
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error while reading survey response with id {id}.", responseId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while reading survey response with id {id}.", responseId);
                return null;
            }
        }
        private async Task<bool> ReGenerateInsight(string responseId, Message e)
        {
            try
            {
                var response = await GetResponseByResponseId(responseId);
                if (response is null)
                {
                    _logger.LogError($"No survey response found with ResponseId: {responseId}");
                    return false;
                }
                var surveyPkg = SurveyPkgInstance.Instance;
                if (surveyPkg is null)
                {
                    _logger.LogError("Server-side Error: No active survey found.");
                    return false;
                }
                var surveyJson = surveyPkg.GetSurvey(response.SurveyVersion).GetSpecificSurveyJsonByQQId(response.QQId);
                if (surveyJson is null)
                {
                    _logger.LogError($"Cannot get survey with version {response.SurveyVersion}");
                    return false;
                }
                var llmTool = new LLMTools(_configuration, _loggerFactory.CreateLogger<LLMTools>());
                if (!llmTool.IsAvailable)
                {
                    _logger.LogError("AI Insight unavailable");
                    await SendMessageWithAt(e.Endpoint, e.UserId, "AI 见解功能目前不可用，请检查日志。");
                    return false;
                }
                var prompt = llmTool.ParseSurveyResponseToNL(surveyJson, response.SurveyAnswer);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    _logger.LogError($"Failed to parse survey response to natural language for responseId: {responseId}");
                    return false;
                }
                var insight = await llmTool.GetInsight(prompt);
                _logger.LogInformation($"Insight generated for responseId: {responseId}");
                // 更新数据库
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("连接字符串未配置。请前往 appsettings.json 添加 \"DefaultConnection\" 连接字符串。");
                    return false;
                }
                const string updateSql = "UPDATE EntranceSurveyResponses SET LLMInsight = @insight WHERE ResponseId = @responseId";
                await using var updateConnection = new MySqlConnection(connStr);
                await updateConnection.OpenAsync();
                await using var updateCmd = new MySqlCommand(updateSql, updateConnection);
                updateCmd.Parameters.AddWithValue("@insight", insight);
                updateCmd.Parameters.AddWithValue("@responseId", responseId);
                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"Insight generated and updated successfully for responseId: {responseId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Failed to update insight for responseId: {responseId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while generating insight for responseId: {responseId}", responseId);
                return false;
            }
        }
        private bool isAdmin(long userId)
        {
            return userId.ToString() == _configuration["Bot:adminId"];
        }

        #region IOnebotService

        public bool IsAvailable { get; private set; } = false;
        public async Task<SendMessageResponseData?> SendMessage(Sisters.WudiLib.Posts.Endpoint endpoint, string message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            try
            {
                return await onebotApi.SendMessageAsync(endpoint, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送消息时发生错误");
                return null;
            }
        }

        public async Task<SendMessageResponseData?> SendMessage(Sisters.WudiLib.Posts.Endpoint endpoint, Sisters.WudiLib.Message message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            try
            {
                return await onebotApi.SendMessageAsync(endpoint, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送消息时发生错误");
                return null;
            }
        }

        public async Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, string message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            try
            {
                var atMsg = SendingMessage.At(userId);
                var sdMsg = new SendingMessage("\n" + message);
                return await onebotApi.SendMessageAsync(endpoint, atMsg + sdMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送消息时发生错误");
                return null;
            }
        }

        public async Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, SendingMessage message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            try
            {
                var atMsg = SendingMessage.At(userId);
                var nlMsg = new SendingMessage("\n");
                return await onebotApi.SendMessageAsync(endpoint, atMsg + nlMsg + message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送消息时发生错误");
                return null;
            }
        }

        public async Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, Sisters.WudiLib.Message message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            return await onebotApi.SendGroupMessageAsync(groupId, message);
        }
        public async Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, string message)
        {

            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            return await onebotApi.SendGroupMessageAsync(groupId, message);
        }
        public async Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, Sisters.WudiLib.Message message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            return await onebotApi.SendPrivateMessageAsync(qqId, message);
        }
        public async Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, string message)
        {
            if (onebotApi is null)
            {
                _logger.LogError("Onebot API 客户端未初始化");
                return null;
            }
            return await onebotApi.SendPrivateMessageAsync(qqId, message);
        }
        #endregion
    }
}
