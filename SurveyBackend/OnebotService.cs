using Microsoft.Extensions.Hosting;
using Sisters.WudiLib;
using Sisters.WudiLib.Builders;
using Sisters.WudiLib.Posts;
using Sisters.WudiLib.Responses;
using Sisters.WudiLib.WebSocket.Reverse;
using System.ComponentModel.Design;
using System.Text.Json;
using Message = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend
{
    public class OnebotService : BackgroundService
    {
        private readonly string _helpText = """
                         Survey Service 帮助
            本系统提供问卷调查辅助服务。仅可在六同音游部相关群中使用。
            命令指南:
            /survey get <问卷标识符> - 注册用户并获取问卷链接
            /survey review <Response Id> - 获取指定回应的审查链接
            
            /survey get <问卷标识符> 指令可以简写为 /survey <问卷标识符>。

            =======================================================
            你可以在 https://github.com/ltyyb/SurveyBackend 获取后端源码
            Powered by Aunt Studio
            Using ASP.NET Core & Sisters.WudiLib
            -
            Developed by Aunt_nuozhen with ❤
            """;
        private readonly ILogger<OnebotService> _logger;
        private readonly IConfiguration _configuration;
        private string? connStr;
        private HttpApiClient? onebotApi = null;

        public OnebotService(ILogger<OnebotService> logger, IConfiguration configuration)
        {
            _logger = logger;
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

            var reverseWSServer = new ReverseWebSocketServer(21568);
            reverseWSServer.SetListenerAuthenticationAndConfiguration((listener, selfId) =>
            {
                onebotApi = listener.ApiClient;
                listener.SocketDisconnected += () =>
                {
                    _logger.LogWarning("WebSocket连接已断开");
                };

                listener.MessageEvent += async (api, e) =>
                {

                    if (e.Content.Text.StartsWith("/survey"))
                    {
                        _logger.LogInformation("Get survey cmd");
                        await SurveyCmdProcesser(e);
                    }
                    _logger.LogInformation(e.Endpoint.ToString());
                    _logger.LogInformation(e.Content.Text);
                };
            }, "testament");
            reverseWSServer.Start(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("后台服务运行中：{time}", DateTimeOffset.Now);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // 每1分钟执行一次
            }

            _logger.LogInformation("后台服务已停止");
        }
        private async Task SurveyCmdProcesser(Message e)
        {
            // 将信息以空格分割
            var args = e.Content.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length < 2)
            {
                // 发送帮助信息
                await SendMessage(e.Endpoint, _helpText);
                return;
            }
            else if (args.Length == 2)
            {
                var surveyId = args[1];
                if (string.IsNullOrWhiteSpace(surveyId))
                {
                    // 发送帮助信息
                    await SendMessage(e.Endpoint, _helpText);
                    return;
                }
                // 等价 get 指令
                if (surveyId == "entr")
                {
                    var qqId = e.UserId;
                    await SendMessage(e.Endpoint, "正在注册用户...");
                    var surveyUser = await SurveyUser.GetUserByQQIdAsync(qqId.ToString(), _logger, connStr!);
                    if (surveyUser is null)
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "注册用户失败，请联系管理员。\n surveyUser is null.");
                        return;
                    }

                    if (await surveyUser.IsUserExisted(connStr!))
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "用户已存在。跳过注册。");
                        surveyUser = await SurveyUser.CreateUserByQQId(qqId.ToString(), connStr!);
                        if (surveyUser is null)
                        {
                            _logger.LogWarning("Cannot get user info from qqId {qqId}", qqId.ToString());
                            await SendMessageWithAt(e.Endpoint, e.UserId, "无法获取已有用户信息。请联系管理员。");
                            return;
                        }
                    }
                    else if (!await surveyUser.RegisterAsync(_logger, connStr!))
                    {
                        await SendMessageWithAt(e.Endpoint, e.UserId, "注册用户失败，请联系管理员。\n Cannot register user.");
                        return;
                    }

                    var link = $"https://ltyyb.auntstudio.com/survey/entr?userId={surveyUser.UserId}";
                    var atMessage = SendingMessage.At(e.UserId);
                    var message = new SendingMessage($"""

                        已成功注册 Survey 用户。
                        请访问链接下方链接:
                        
                        {link}

                        完成问卷。
                        如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。

                        请注意看清链接所属用户，请勿填写他人问卷链接。
                        """);
                    await SendMessage(e.Endpoint, atMessage + message);
                }
                else
                {
                    // 发送帮助信息
                    var errorMsg = new SendingMessage("未知的指令。\n================\n\n");
                    await SendMessage(e.Endpoint, errorMsg + _helpText);
                    return;
                }

            }
            else if (args.Length == 3)
            {
                if (args[1] == "get")
                {
                    var surveyId = args[2];
                    if (string.IsNullOrWhiteSpace(surveyId))
                    {
                        // 发送帮助信息
                        await SendMessage(e.Endpoint, _helpText);
                        return;
                    }
                    // 等价 get 指令
                    if (surveyId == "entr")
                    {
                        var qqId = e.UserId;
                        await SendMessageWithAt(e.Endpoint, e.UserId, "正在注册用户...");
                        var surveyUser = await SurveyUser.CreateUserByQQId(qqId.ToString(), connStr!);
                        if (surveyUser is null)
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "注册用户失败，请联系管理员。\n surveyUser is null.");
                            return;
                        }

                        if (await surveyUser.IsUserExisted(connStr!))
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "用户已存在。跳过注册。");
                            surveyUser = await SurveyUser.GetUserByQQIdAsync(qqId.ToString(), _logger, connStr!);
                            if (surveyUser is null)
                            {
                                _logger.LogWarning("Cannot get user info from qqId {qqId}", qqId.ToString());
                                await SendMessageWithAt(e.Endpoint, e.UserId, "无法获取已有用户信息。请联系管理员。");
                                return;
                            }
                        }
                        else if (!await surveyUser.RegisterAsync(_logger, connStr!))
                        {
                            await SendMessageWithAt(e.Endpoint, e.UserId, "注册用户失败，请联系管理员。\n Cannot register user.");
                            return;
                        }
                        

                        var link = $"https://ltyyb.auntstudio.com/survey/entr?userId={surveyUser.UserId}";
                        var message = new SendingMessage($"""

                        已成功注册 Survey 用户。
                        请访问链接下方链接:
                        
                        {link}

                        完成问卷。
                        如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。

                        请注意看清链接所属用户，请勿填写他人问卷链接。
                        """);
                        await SendMessageWithAt(e.Endpoint, e.UserId, message);
                    }
                }
                else if (args[1] == "review")
                {
                    var responseId = args[2];
                    var link = $"https://ltyyb.auntstudio.com/survey/entr/review?surveyId={responseId}";
                    var message = new SendingMessage($"""
                        审阅链接:
                        
                        {link}

                        如复制到浏览器中访问，请务必确保链接完整。请不要修改链接任何内容。

                        请不要将本链接页面中的任何内容分享给他人。
                        
                        未确认 Response Id 有效性，如遇问题请检查提供的 ResponseId 是否正确。
                        """);
                    await SendMessageWithAt(e.Endpoint, e.UserId, message);
                }
                else
                {
                    // 发送帮助信息
                    var errorMsg = new SendingMessage("未知的指令。\n================\n\n");
                    await SendMessageWithAt(e.Endpoint, e.UserId, errorMsg + _helpText);
                    return;
                }
            }
        }
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

        public async Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId ,string message)
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

        public async Task<SendMessageResponseData?> SendMessageWithAt(Sisters.WudiLib.Posts.Endpoint endpoint, long userId, Sisters.WudiLib.Message message)
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

        //    public async Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, Sisters.WudiLib.Message message)
        //    {
        //        if (onebotApi is null)
        //        {
        //            _logger.LogError("Onebot API 客户端未初始化");
        //            return null;
        //        }
        //        return await onebotApi.SendGroupMessageAsync(groupId, message);
        //    }
        //    public async Task<SendGroupMessageResponseData?> SendGroupMessageAsync(long groupId, string message)
        //    {

        //        if (onebotApi is null)
        //        {
        //            _logger.LogError("Onebot API 客户端未初始化");
        //            return null;
        //        }
        //        return await onebotApi.SendGroupMessageAsync(groupId, message);
        //    }
        //    public async Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, Sisters.WudiLib.Message message)
        //    {
        //        if (onebotApi is null)
        //        {
        //            _logger.LogError("Onebot API 客户端未初始化");
        //            return null;
        //        }
        //        return await onebotApi.SendPrivateMessageAsync(qqId, message);
        //    }
        //    public async Task<SendPrivateMessageResponseData?> SendPrivateMessageAsync(long qqId, string message)
        //    {
        //        if (onebotApi is null)
        //        {
        //            _logger.LogError("Onebot API 客户端未初始化");
        //            return null;
        //        }
        //        return await onebotApi.SendPrivateMessageAsync(qqId, message);
        //    }

    }
}
