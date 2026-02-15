# Survey Backend for ltyyb

[![wakatime](https://wakatime.com/badge/user/486c5b5b-ef54-48dd-a69c-bacb70bf3113/project/6edfb7f5-f587-44a5-9a66-b746fd2086e6.svg)](https://wakatime.com/badge/user/486c5b5b-ef54-48dd-a69c-bacb70bf3113/project/6edfb7f5-f587-44a5-9a66-b746fd2086e6)

适用于厦门六中同安校区音游部的入群问卷调查后端。现已更通用化，并提供高可自定义配置。

基于 ASP.NET Core 10.0 。与前端连接部分提供问卷题目的读取和问卷结果的提交接口等，并与群内机器人联动，实现自动推送问卷、众审投票等功能。详见[审核流程参照](#审核流程参照)。

---

从 `v3` 版本开始本系统已使用 EF Core 管理数据库操作，且 `v4`/`v3` 均发生了较大的数据库结构变更，从更低版本升级请务必自行完成迁移，并注意备份数据。

> [!TIP]
> `v2` 及更低版本存档在 [`legacy` 分支](https://github.com/ltyyb/SurveyBackend/tree/legacy) 。

## 快速开始

### 从一般构建中启动

一般构建从 `main` 分支中编译发行，具有更好的稳定性。

1. 安装 [ASP.NET Core 运行时 10.0 或更高版本](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)。

2. 前往 Main Build Action 页面: [Main Build Action](https://github.com/ltyyb/SurveyBackend/actions/workflows/main-build.yml)。

3. 选择最新一次运行记录。

4. 在底部找到 `Artifacts`, 根据操作系统环境选择构建版本 `SurveyBackend-x.x.x+abcdefg-xxxxxxx-x64-x64`, 例如 Linux 环境选择 `SurveyBackend-4.2.1+c07af04-linux-x64-x64`，Windows 选择 `SurveyBackend-4.2.1+c07af04-win-x64-x64`。

5. 点击下载按钮下载构建包。

6. 解压到服务器任意目录下。

7. 参考 [数据库配置](#数据库配置) 配置数据库。

8. 打开 `appsettings.json` 文件，参考 [配置文件](#配置文件) 修改程序配置。

9. 运行程序。

### 从 Dev 版构建启动

Dev Build 从 `dev` 分支编译发行，包含最新且可能未经测试的更改，无法保证稳定性，请仅在测试环境使用。

1. 安装 [ASP.NET Core 运行时 10.0 或更高版本](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)。

2. 前往 Dev Build Action 页面: [Dev Build Action](https://github.com/ltyyb/SurveyBackend/actions/workflows/dev-build.yml)。

3. 选择最新一次运行记录。

4. 在底部找到 `Artifacts`, 根据操作系统环境选择构建版本 `SurveyBackend-x.x.x+abcdefg-xxxxxxx-x64-x64`, 例如 Linux 环境选择 `SurveyBackend-4.2.1+c07af04-linux-x64-x64`，Windows 选择 `SurveyBackend-4.2.1+c07af04-win-x64-x64`。

5. 点击下载按钮下载构建包。

6. 解压到服务器任意目录下。

7. 参考 [数据库配置](#数据库配置) 配置数据库。

8. 打开 `appsettings.json` 文件，参考 [配置文件](#配置文件) 修改程序配置。

9. 运行程序。

### 从源代码中启动

请确保您已安装[.NET SDK 10.0 或更高版本](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)。

您可以自行选择分支并克隆:

```bash
git clone -b <branch> https://github.com/SurveyBackend/SurveyBackend.git
```

导航至主项目:

```bash
cd SurveyBackend/SurveyBackend
```

参考 [数据库配置](#数据库配置) 和 [配置文件](#配置文件) 配置数据库和 `appsettings` .

构建并运行程序:
```bash
dotnet run
```


## 数据库配置

本项目使用 MySQL 作为数据库。将由 EF Core 自动管理，但不会自动进行迁移操作。 

请执行以下命令生成迁移 SQL 指令:
```bash
dotnet ef migrations script -o ./migrations.sql
```
再将 `./migrations.sql` 在你的 MySQL 数据库服务端执行。

> [!WARNING]
> 本项目使用 `MySql.EntityFrameworkCore` 作为 EF Core Provider, 该 Provider 无法正常生成幂等的 SQL 脚本，因此请使用干净的 MySQL 数据库执行迁移脚本。必要时请检查生成的 `migrations.sql` 文件，确保其中的 SQL 指令符合预期。
> 
> 运行脚本前请务必做好备份！！

> 当 [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql) 适配 EF Core 10.0 后，我们计划迁移到该 Provider，以获得更好的性能和更稳定的迁移支持。


## 配置文件

你需要修改 `appsettings.json` 以配置数据库连接字符串等其它杂项配置。

以下是配置文件详解，**请在配置完毕后删除所有注释**或参考仓库内的 `appsettings.json` [示例文件](https://github.com/ltyyb/SurveyBackend/blob/master/SurveyBackend/appsettings.json)。

```json
{
  // ASP.NET Core 默认配置
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",

  // 数据库连接字符串
  "ConnectionStrings": {
    "DefaultConnection": "Server=<YourServerAddrOrIp>;Port=<MySqlServerPort>;Database=<YourDatabaseName>;User=<YourUsername>;Password=<YourPassword>;charset=utf8mb4;SslMode=Required"
  }, // 可以修改SslMode为None以禁用SSL连接

  // 符合 OneBot v11 标准的 QQ 机器人配置
  // 连接方式为反向ws连接, 即本程序启动ws服务器供 OneBot 协议端连接
  "Bot": {
    "accessToken": "<Your AccessToken>", // ws连接的accessToken, 你可以自己定义
    "wsPort": 21568, // ws服务器端口
    "mainGroupId": "23********1", // 主群号, 更多信息请参考审核流程参照
    "verifyGroupId": "21******59", // 审核群群号, 更多信息请参考审核流程参照
    "adminId": "56******0" // 管理员ID，将自动在users表中设置身份组为 SuperAdmin
  },

  // AI 见解配置
  "LLM": {
    "ModelName": "gpt-4.1", // 使用的 OpenAI 模型名称
    "OpenAIKey": "sk-****************************", // OpenAI API Key
    "OpenAIEndpoint": "https://api.openai.com/v1", // OpenAI API 基础地址
    "SysPromptPath": "sysPrompt.txt" // 系统提示词文件路径
  },
  "API": {
    "Endpoint": "https://api.example.com/", // 后端 API 基础地址, 暂时无用，可保留此示例字段
    "SurveyLinkEndpoint": "https://example.com/survey/" // 问卷链接基础地址，请在此链接放置你的问卷填写前端页面
  },
  
  // 是否暂停服务
  "IsDisabled": "false"
}
```

> [!CAUTION]
> 在程序运行时修改配置文件是**极其不推荐的**。因程序配置了 `reloadOnChange` ，修改配置文件后尽管可以在部分组件上实时生效，但各个组件间的配置状态可能会不一致，导致不可预期的错误。
> 
> 与此同时，对配置文件合法性的强制检查仅在程序运行之初。如果配置文件修改出现错误可能导致某个组件无法恢复正常工作或引发不可预期的异常。
> 
> 因此请在修改配置文件后重启程序。

## 指令指南

你可以在会话中使用 `/survey` 指令来测试机器人是否连接正常以及查看可用的子指令列表。

## 审核流程参照

现有已配置的主群 M 和审核群 V，以及机器人 B，管理员 A。

### 新用户填写问卷流程

1. 新用户加入审核群 V , 发送 `/survey start`

2. 后端查询数据库，获取所有 `IsVerifySurvey == true` 的 Survey, 如有多个则要求用户选择，如 `/survey start 2`；如仅单个则跳过。

3. 本程序尝试为该 QQ 号注册随机的 UserId 并写入数据库 (如果已存在则直接在数据库中查询)。

4. 后端生成一个 Request 并写入数据库, 这个 Request 记录将记录用户，并把 RequestId 和 Survey 最新的 Questionnaire 的 Id 包装进 URL 参数中，将链接发送给用户。

5. 用户打开链接，前端向后端请求问卷题面，后端向前端提供最新版本的问卷题面。前端 `Survey.js` 渲染问卷。用户填写问卷。

6. 用户点击提交问卷后，前端将 UserId 、QuestionnaireId、填写结果 POST 给后端。后端将结果写入数据库，并生成一个 Response ID。

7. 如果 AI 见解可用，将同时生成 AI 见解并存入数据库。

8. 后端通过机器人 B 执行如下操作：
    - 在 V 中向用户发送提交成功的消息
    - 在 M 中推送问卷审阅链接与投票指令等
    - 在 M 中推送 AI 见解（如果可用）

9. 用户等待审核结果。

### 主群用户投票流程

1. 主群 M 中的已审核用户收到新提交推送，可以打开链接查看新用户的提交结果。

2. 主群 M 中的已审核用户可以发送 `/survey vote <SubmissionId> a` (同意) 或 `/survey vote <SubmissionId> d` (拒绝) 指令进行投票。

3. 后端收到信息，判断是否为短的8位 SubmissionId ，如果是则转换为完整的 SubmissionId。随后将投票纪录写入数据库。

4. 向用户推送投票成功反馈。

### 后台服务检查

#### 未推送/未审核提交检查 | `BackgroudPushingService`

该后台服务每隔3小时执行一轮如下检查: 

  1. 每隔 10 分钟检查一次 `ReviewSubmissions` DbSet，查找 `r.Status == ReviewStatus.Pending` 的所有提交。
  2. 对于每一条未审核的提交，再次将审核消息推送到主群以达到催审目的。

每次循环时，如果出现以下情况可能影响推送节律: 

  1. OneBot 协议端未连接或不可用，将在 15s 后重新检查。
  2. 上次收到任意消息的间隔超过 48 小时，将跳过推送检查，停留 6h 后重新开始下一轮循环。
  3. 当前时间不在推送时间段内（9:00-23:00），将跳过推送检查，停留 1h 后重新开始下一轮循环。

#### 审核结果检查 | `BackgroundVerifyService`

该后台服务每隔 10 分钟执行一轮未审核问卷判定检查以及审核未通过问卷的清理。以下是详细流程

**未审核问卷判定检查**

  1. 检查 `ReviewSubmissions` DbSet，查找 `r.Status == ReviewStatus.Pending` 的提交，计算其在 `ReviewVotes` 表中的投票结果。
  2. 对于每一条未审核的提交，计算其同意票与拒绝票的数量。
  3. 如果总投票数 ≥ 5 张，尝试计算同意率。
  4. 如果同意率达到 60% 以上，则判定审核通过，否则不通过。
  5. 如果审核通过，执行如下操作: 
      - 将 `r.Status` 设置为 `ReviewStatus.Approved`
      - 将该用户的 `UserGroup` 设置为 `UserGroup.VerifiedUser`。
      - 通过机器人 B 向用户发送审核通过消息，并附上主群 M 的群号
  6. 如果审核未通过，执行如下操作: 
      - 将 `r.Status` 设置为 `ReviewStatus.Rejected`
      - 将该用户的 `UserGroup` 设置为 `UserGroup.NewComer`。
      - 通过机器人 B 向用户发送审核未通过消息。
      - 将该问卷提交记录添加到待删除提交列表中，并记录目标删除时间（当前时间 + 24 小时），在 24 小时后通过下方的清理流程删除。 

**审核未通过问卷清理**

  1. 检查待删除提交列表，查找目标删除时间小于当前时间的提交。
  2. 对于每条将删除提交，将直接从 `Submissions` 表中删除该提交记录。
  3. 由于其它表中设置了 `DeleteBehavior.Cascade`，相关的记录也会被级联删除。

## AI 见解 (LLM Insight)

如果配置了 OpenAI Key 和系统提示词文件，后台服务会在用户提交问卷后尝试生成 AI 见解。

你需要配置 `appsettings.json` 中的 `LLM` 节点以启用该功能。

你可能注意到你需要一个系统提示词文件。你可以参考以下示例内容:

```
你是一个AI问卷审阅者。你正在审阅厦门六中同安校区音游部的新生入群问卷。本音游部是一个包容性较强的社群，不必过多考虑问卷填写者的音游相关实力。但我们希望创造一个和谐的讨论氛围并尽量隔离成绩造假行为的出现。所以我们使用了这一问卷审核制度。

你现在需要根据以下要点，给出问卷评分及相关见解：
    1. 评分范围为0-100分，如无明显问题的问卷不应评定低于75分。
    2. 提供的问卷可能为自然语言形式，我将提供给你 Part 2 - Part 3 的问题以及用户的填写。Part 2 与 3 的作答可能为混合提供。
    3. Part 2 的填写内容均为音游素养相关的问题，请注意该部分的审核，不必过多考虑问卷填写者的音游相关实力，
        但如果发现其填写内容中有明显的前后题目选择不一致或可能存在作假或虚填行为，请酌情扣分并在见解中指出。
        例如，某个用户填写了擅长/喜爱的音乐游戏玩法种类，但在勾选其曾经/现在接触过的音乐游戏却没有该种玩法，且该情况多次出现，应考虑扣分。
        请注意，用户不一定填写自己的音游水平量化值，若某个音游PTT/RKS/Level 为 0 则表示用户不愿意透露此数据。 请勿以此为评分依据。
        此部分评分重点在于用户的作答是否合理、前后是否一致、是否有明显的作假嫌疑，不建议以参与度低作为扣分理由。
        请注意，除了"请在下方填写您其它音游的潜力值或其他能代表您该游戏水平的指标"题目(如果有)以外，该部分表述均为预设的CheckBox题，即回答表述均为预设，请勿以规范表达为由扣分。
    4. Part 3 的填写内容为成员素质保证测试，如在其中可能有违反社群规定和NSFW内容倾向的选择，请酌情扣分并在见解中指出。
    5. 你需要在回答的开头直接点明你的分数，并在见解中简单总结该用户的填写情况，在这之后指出扣分的原因。
    6. 你的回答不应该为 Markdown 格式，善用换行符。

稍后 user 将提供问卷内容，请你根据上述要求进行评分和见解分析。
```

将此文本放入项目可执行文件旁边的 `sysPrompt.txt` 或其他你已经在配置文件中指定的路径中即可。

应根据实际需要以及问卷实况进行微调。

> [!TIP]
> 你知道吗？你可以使用 `Utilities` 项目中的 `LLMTools` 来测试你的系统提示词文件的效果。
> 
> 但你需要手动设置 User Secret 以及写入程序的系统提示词等配置并在调试环境下进行测试。
>
> 详见[`Utilities` 项目内的 `readme.md`](https://github.com/ltyyb/SurveyBackend/blob/master/Utilities/readme.md)
> 
> v4 后没时间改 `Utilities` 项目了，所以可能得等等。。

## 许可证

本项目采用 MIT 许可证，详情请参见 `LICENSE.txt` 文件。

Copyright © 2026 [厦门六中同安校区音游部](https://github.com/ltyyb) & [Aunt Studio](https://github.com/Aunt-Studio)
