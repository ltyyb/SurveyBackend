using MessageContext = Sisters.WudiLib.Posts.Message;

namespace SurveyBackend.Bot.Commands;

public class DisableSystemCommand : AuthorizedAsyncCommand
{
    public override string CommandName => "disable-system";
    public override bool IsSuperCommand => true;
    public override string[] Aliases => ["shutdown", "ds"];
    public override string Description => "使用方法: /survey disable-system \n 临时关闭问卷系统除本命令外的所有调用。带参调用查看详情。";
    public override UserGroup[] RequiredPermission => [UserGroup.Admin, UserGroup.SuperAdmin];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SurveyCommandRegistry _surveyCommandRegistry;
    public DisableSystemCommand(IServiceScopeFactory serviceScopeFactory,
                                ILoggerFactory loggerFactory, SurveyCommandRegistry surveyCommandRegistry) : base(serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _loggerFactory = loggerFactory;
        _surveyCommandRegistry = surveyCommandRegistry;
    }
    protected async override Task<CommandResponse?> ExecuteAuthorizedAsync(MessageContext context, string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            if (!_surveyCommandRegistry.IsSystemDisabled)
            {
                _surveyCommandRegistry.IsSystemDisabled = true;
                return CommandResponse.SuccessResponse("已成功关闭所有命令路由。再次执行本命令以恢复。");
            }
            else
            {
                _surveyCommandRegistry.IsSystemDisabled = false;
                return CommandResponse.SuccessResponse(messageText: "已成功恢复所有命令路由。");
            }
        }
        else
        {
            var msg = """
                参数不正确。
                本命令用于临时关闭问卷系统除本命令外的所有命令路由。
                与 appsettings.json 里的配置不互通。其优先级高于本命令。

                使用方法:
                /survey disable-system
                """;
            return CommandResponse.SuccessResponse(msg);
        }
    }
}
