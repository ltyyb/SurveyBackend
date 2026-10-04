# Survey Backend for ltyyb

[![wakatime](https://wakatime.com/badge/user/486c5b5b-ef54-48dd-a69c-bacb70bf3113/project/6edfb7f5-f587-44a5-9a66-b746fd2086e6.svg)](https://wakatime.com/badge/user/486c5b5b-ef54-48dd-a69c-bacb70bf3113/project/6edfb7f5-f587-44a5-9a66-b746fd2086e6)

适用于厦门六中同安校区音游部的入群问卷调查后端。现已更通用化，并提供高可自定义配置。

基于 ASP.NET Core 10.0 。与前端连接部分提供问卷题目的读取和问卷结果的提交接口等，并与群内机器人联动，实现自动推送问卷、众审投票等功能。详见[审核流程参照](#审核流程参照)。

---

从 `v5` 版本开始本系统从 MySQL 迁移到 SQLite，从 `v4` 版本升级请先备份数据，然后按照 [迁移指南](#从-v4-及以下版本迁移---从-mysql-dump-导入) 迁移数据库。

> [!TIP]
> `v4` 及更低版本存档在 [`legacy/v4` 分支](https://github.com/ltyyb/SurveyBackend/tree/legacy/v4) 。

> [!TIP]
> `v2` 及更低版本存档在 [`legacy/v2` 分支](https://github.com/ltyyb/SurveyBackend/tree/legacy/v2) 。
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

### 从 Docker 中启动

仓库提供多阶段构建的 `Dockerfile`，可自行构建，也可使用 [Docker Build 工作流](https://github.com/ltyyb/SurveyBackend/actions/workflows/docker-build.yml) 发布到 GitHub Container Registry（GHCR）的镜像。宿主机只需安装并启动 Docker；Windows 请使用 Docker Desktop 的 Linux 容器模式，无需另行安装 .NET 运行时。

以下命令均在仓库根目录执行。Linux / macOS 示例使用 Bash，Windows 示例使用 PowerShell；两种 Shell 的路径写法和续行符不同，请使用对应示例。

#### 1. 准备配置和数据目录

克隆仓库并进入目录，默认使用 `main` 分支；测试 Dev 版本时可将分支改为 `dev`：

```bash
git clone -b main https://github.com/ltyyb/SurveyBackend.git
cd SurveyBackend
```

Linux / macOS：

```bash
mkdir -p storage
cp src/SurveyBackend/appsettings.example.json src/SurveyBackend/appsettings.json
```

Windows PowerShell：

```powershell
New-Item -ItemType Directory -Force storage | Out-Null
Copy-Item src/SurveyBackend/appsettings.example.json src/SurveyBackend/appsettings.json
```

若已有 `appsettings.json`，请直接修改，避免再次复制覆盖。按照 [配置文件](#配置文件) 填写 OneBot 的 AccessToken、群号、管理员 QQ 号，以及问卷前端地址。旧配置中的 `ConnectionStrings:DefaultConnection` 必须删除。

容器内路径与宿主机路径不同：

| 用途 | 宿主机路径 | 容器内路径 |
| --- | --- | --- |
| 配置文件（只读） | `src/SurveyBackend/appsettings.json` | `/app/appsettings.json` |
| SQLite 数据目录（可写） | `storage/` | `/data/` |
| AI 系统提示词（可选，只读） | 自行创建的 `sysPrompt.txt` | `/app/sysPrompt.txt` |

镜像设置 `Database__Path=/data/data.db`，优先于 JSON 中的 `Database:Path`，因此示例配置中的 `data.db` 无需修改。若要更换数据库路径，应同时调整环境变量并确保目标仍位于持久化目录内。

新部署保持 `storage/` 为空，首次启动会自动建库。已有 SQLite 数据库必须在首次启动前放入 `storage/data.db`；从 v4 升级请先按 [迁移指南](#从-v4-及以下版本迁移---从-mysql-dump-导入) 导入，或使用下方的容器导入命令。复制数据库前应停止原服务，备份时保留仍存在的 `-wal` / `-shm` 文件。

镜像以非 root 用户运行。Linux 宿主机需给数据目录及已有数据库设置写权限：

```bash
sudo chown -R 1654:1654 storage
sudo chmod 700 storage
```

`1654` 是所用 .NET 基础镜像的默认 `APP_UID`，详见 [Microsoft 容器用户说明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/app-user)。更换运行用户或基础镜像时应按实际 UID 调整。配置及提示词文件也必须允许容器用户读取；Docker Desktop 应允许共享该宿主目录。

#### 2. 获取镜像

Docker Build 工作流在 `main` / `dev` 分支推送时构建并发布 `linux/amd64` 镜像，也支持在 Actions 页面手动运行。PR 仅检查构建，不登录或推送镜像；其他分支上的手动运行也仅构建。

| 分支 | 镜像标签 | 用途 |
| --- | --- | --- |
| `main` | `ghcr.io/ltyyb/surveybackend:latest` | 主分支最新构建 |
| `dev` | `ghcr.io/ltyyb/surveybackend:dev` | 开发版，仅建议测试使用 |
| `main` | `ghcr.io/ltyyb/surveybackend:5.0.0-<12位提交号>` | 按 `version.txt` 和提交号定位构建 |
| `dev` | `ghcr.io/ltyyb/surveybackend:5.0.0-dev-<12位提交号>` | 按版本和提交号定位开发构建 |

上表的 `5.0.0` 仅为版本示例。工作流使用内置 `GITHUB_TOKEN` 发布，无需配置 Docker Hub 密钥；发布方式参见 [GitHub 镜像发布说明](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)。首次成功发布后，请在仓库关联的 Package 设置中确认镜像可见性：需要允许匿名拉取时，将 Package 设为 public；私有镜像需先用具有 `read:packages` 权限的凭据登录 `ghcr.io`。Fork 仓库发布的镜像地址随仓库所有者和名称变化，统一转为小写。

首次发布完成后，可拉取主分支镜像，并给它添加本地标签，以直接使用下方的启动示例：

```bash
docker pull ghcr.io/ltyyb/surveybackend:latest
docker tag ghcr.io/ltyyb/surveybackend:latest survey-backend:local
```

使用 Dev 版时将 `latest` 换成 `dev`；固定部署版本时，使用对应的版本及提交号标签。也可以将启动命令末尾的 `survey-backend:local` 直接替换为完整 GHCR 镜像地址。

自行构建则执行：

```bash
docker build -t survey-backend:local .
```

首次构建需要联网下载 .NET 基础镜像和 NuGet 依赖。`.dockerignore` 已排除本地 `appsettings.json`、数据库和 `db_dump.sql`；这些文件需在运行时挂载。自定义提示词同样建议放在构建上下文之外，避免将内容打包进镜像。

#### 3. 启动容器

Linux / macOS：

```bash
docker run -d --name survey-backend \
  --restart unless-stopped \
  -p 8080:8080 -p 21568:21568 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_HTTP_PORTS=8080 \
  -e TZ=Asia/Shanghai \
  --mount "type=bind,src=$(pwd)/storage,dst=/data" \
  --mount "type=bind,src=$(pwd)/src/SurveyBackend/appsettings.json,dst=/app/appsettings.json,readonly" \
  survey-backend:local
```

Windows PowerShell：

```powershell
docker run -d --name survey-backend `
  --restart unless-stopped `
  -p 8080:8080 -p 21568:21568 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e ASPNETCORE_HTTP_PORTS=8080 `
  -e TZ=Asia/Shanghai `
  --mount "type=bind,src=${PWD}/storage,dst=/data" `
  --mount "type=bind,src=${PWD}/src/SurveyBackend/appsettings.json,dst=/app/appsettings.json,readonly" `
  survey-backend:local
```

示例使用 [Docker bind mount](https://docs.docker.com/engine/storage/bind-mounts/)，挂载前必须创建宿主目录和配置文件。请挂载整个 `/data`，以保存数据库及 WAL 辅助文件；不要只挂载 `data.db`，也不要用宿主目录覆盖整个 `/app`。

| 端口映射 | 用途 |
| --- | --- |
| `8080:8080` | HTTP API，访问地址为 `http://<宿主机地址>:8080` |
| `21568:21568` | OneBot v11 反向 WebSocket，容器端口对应 `Bot:wsPort` |

冒号左侧是宿主机端口，右侧是容器端口。宿主机 8080 被占用时，可改为 `-p 18080:8080`；修改 `Bot:wsPort` 时必须同步修改 WebSocket 映射的右侧端口。`TZ=Asia/Shanghai` 用于本地时间和推送时段，数据库时间仍按 UTC 处理。

镜像中的 `EXPOSE 8081` 不会自动配置 HTTPS；上述示例仅启用 HTTP 8080。生产部署可由反向代理提供 HTTPS，并将 API 转发至 8080；代理和后端同机时，可将映射改为 `-p 127.0.0.1:8080:8080`。直接在容器内启用 HTTPS 则需另外配置证书和 Kestrel，不能只添加端口映射。HTTP 端口设置参见 [Microsoft 容器端口说明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/aspnet-port)。

启用 AI 见解时，先按 [AI 见解](#ai-见解-llm-insight) 创建系统提示词，将 `LLM:SysPromptPath` 设置为 `/app/sysPrompt.txt`，并在启动命令的镜像名之前增加挂载参数：

```text
--mount "type=bind,src=<宿主机提示词文件的绝对路径>,dst=/app/sysPrompt.txt,readonly"
```

不用 AI 见解时可将 `LLM:OpenAIKey` 留空，并省略提示词挂载，不影响问卷及审核功能。

#### 4. 连接 OneBot 并检查运行状态

在 OneBot 协议端配置反向 WebSocket 地址 `ws://<宿主机地址>:21568/`，AccessToken 与 `Bot:accessToken` 保持一致，并确保该端口可以从协议端所在机器访问。同一宿主机上直接运行的协议端可使用 `ws://127.0.0.1:21568/`；协议端在另一容器内时，`127.0.0.1` 指向该容器自身，应使用可达的宿主机地址，或在共享 Docker 网络中使用后端容器名和容器端口。

```bash
docker ps --filter name=survey-backend
docker logs --tail 100 -f survey-backend
```

日志应显示数据库路径 `/data/data.db`、HTTP 监听端口和 OneBot 反向 WebSocket 服务器启动情况。连接后，在测试群发送 `/survey`，检查指令回复；HTTP 接口可使用已有问卷 ID 请求 `/api/Survey/<questionnaireId>`，并在 `SURVEY-USER-ID` 请求头中提供已有用户 ID。新库尚无问卷，需要先创建问卷再测试提交。生产环境不提供 `/openapi/v1.json`，访问根路径返回 404 也不代表启动失败。

#### 5. 日常维护、备份和升级

修改配置或提示词后重启容器；修改端口、环境变量或挂载参数时，需要删除旧容器并按新参数重新创建：

```bash
docker restart survey-backend
docker stop survey-backend
docker start survey-backend
```

这三条分别用于重启、停止和启动，按需要执行。重建时复用原 `storage/`，删除容器不会删除该宿主目录。

升级前先停止容器，将整个 `storage/` 复制到独立的备份位置，同时保存配置和提示词。不要只复制运行中的 `data.db`。使用 GHCR 时重新执行第 2 步的 `docker pull` 和 `docker tag`；自行构建时更新源码并执行 `docker build -t survey-backend:local .`。`docker restart` 不会让旧容器使用新镜像。

如果新版本包含数据库迁移，先按 [数据库配置](#数据库配置) 生成并审阅迁移 SQL，再使用新镜像对已备份的数据库执行一次性迁移。以下为 Bash 示例，PowerShell 请使用反引号续行并将 `$(pwd)` 替换为 `${PWD}`：

```bash
docker run --rm \
  --mount "type=bind,src=$(pwd)/storage,dst=/data" \
  survey-backend:local --migrate-database /data/data.db
```

此命令使用镜像已有的入口执行迁移，无需额外写 `dotnet SurveyBackend.dll`，也不需要 Bot 配置或端口映射。确认命令成功退出后，删除已停止的旧容器，再执行第 3 步（或下方 Compose 启动命令）：

```bash
docker rm survey-backend
```

迁移失败时保持服务停止并检查原因。回退到旧镜像时，应同时恢复与旧版本匹配的数据库备份。

若需完全通过容器从 MySQL Dump 导入，可在首次启动前执行：

```bash
docker run --rm \
  --mount "type=bind,src=$(pwd)/db_dump.sql,dst=/import/db_dump.sql,readonly" \
  --mount "type=bind,src=$(pwd)/storage,dst=/data" \
  survey-backend:local --import-mysql-dump /import/db_dump.sql /data/data.db
```

目标 `storage/data.db` 必须不存在，Dump 文件必须可由容器用户读取；格式限制和校验规则见 [迁移指南](#从-v4-及以下版本迁移---从-mysql-dump-导入)。导入和迁移期间不要运行使用同一数据库的后端实例。

#### 可选：使用 Docker Compose

也可在仓库根目录自行创建 `compose.yaml`，使用相同的配置与持久化目录：

```yaml
services:
  survey-backend:
    build: .
    image: survey-backend:local
    container_name: survey-backend
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_HTTP_PORTS: "8080"
      Database__Path: /data/data.db
      TZ: Asia/Shanghai
    ports:
      - "8080:8080"
      - "21568:21568"
    volumes:
      - type: bind
        source: ./storage
        target: /data
      - type: bind
        source: ./src/SurveyBackend/appsettings.json
        target: /app/appsettings.json
        read_only: true
        bind:
          create_host_path: false
```

需要 AI 见解时，按同样方式增加提示词文件的只读挂载。先完成第 1 步的配置和权限设置，再执行：

```bash
docker compose up -d --build
docker compose logs --tail 100 -f
```

使用 GHCR 镜像时，删除 `build: .` 并将 `image` 改为 `ghcr.io/ltyyb/surveybackend:latest`（或指定版本标签），通过 `docker compose pull` 和 `docker compose up -d` 拉取并启动，无需本地构建。

更新配置或提示词后使用 `docker compose restart`；升级前使用 `docker compose stop` 停止服务并备份，自行构建时先运行 `docker compose build`，使用 GHCR 时先运行 `docker compose pull`，再用新镜像迁移。GHCR 模式迁移命令中的 `survey-backend:local` 应换成 Compose 使用的完整镜像地址。完成后执行 `docker compose up -d`。停止并移除容器使用 `docker compose down`，bind mount 中的 `storage/` 会保留。`docker run` 和 Compose 两种方式任选一种，切换前先移除已有同名容器。

#### 常见问题

| 现象 | 检查方式 |
| --- | --- |
| 配置校验失败或容器反复退出 | 查看 `docker logs survey-backend`，检查真实 Bot / API 配置、审核阈值和旧 MySQL 配置是否已删除 |
| `bind source path does not exist` | 确认挂载源文件存在，命令从仓库根目录执行，路径属于 Docker 服务所在宿主机 |
| SQLite 无法打开或只读 | 检查 `/data` 是否挂载为可写、宿主目录及已有数据库是否允许 UID 1654 写入 |
| 提示存在未应用的迁移 | 停止服务、备份并审阅 SQL，再运行上述一次性迁移命令 |
| OneBot 无法连接 | 检查反向 WebSocket 地址、AccessToken、`Bot:wsPort`、端口映射和防火墙；其他容器内不能用 `127.0.0.1` 指代后端 |
| AI 见解不可用 | 检查 API Key、模型及 Endpoint，并确认 `LLM:SysPromptPath` 指向容器内存在且可读的提示词文件 |

### 从源代码中启动

请确保您已安装[.NET SDK 10.0 或更高版本](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)。

您可以自行选择分支并克隆:

```bash
git clone -b <branch> https://github.com/SurveyBackend/SurveyBackend.git
```

导航至仓库根目录并构建解决方案:

```bash
cd SurveyBackend
dotnet build SurveyBackend.slnx
```

根目录保留 `SurveyBackend.slnx`、文档、版本文件及仓库级开发配置；后端项目、源码和配置示例位于 `src/SurveyBackend/`。可以使用支持 `.slnx` 格式的 IDE 打开根目录下的解决方案，或使用 VS Code 打开仓库根目录。

项目内部按职责组织，每个顶层类型使用独立文件：

```text
src/SurveyBackend/
├── BackgroundServices/          后台推送和审核服务
├── Bot/                         OneBot 服务及接口
│   └── Commands/                机器人指令
│       └── Infrastructure/      指令接口、基类及注册器
├── Configuration/               强类型配置选项
├── Controllers/                 HTTP 接口
├── Data/                        数据库上下文
│   └── Migrations/              EF Core 迁移及模型快照
├── Models/                      数据实体及枚举
├── Services/                    AI 见解和问卷统计
├── Properties/launchSettings.json
├── GlobalUsings.cs
├── Program.cs
├── SurveyBackend.csproj
└── appsettings.example.json
```

源码使用文件级命名空间，以 `SurveyBackend` 为根命名空间并对应项目内的目录。常用导入集中在 `GlobalUsings.cs`，其余导入和类型别名保留在使用它们的文件中。

`tests/SurveyBackend.Tests/` 包含审核判定和配置启动校验的 xUnit 测试，可在根目录运行：

```bash
dotnet test SurveyBackend.slnx -c Release
```

测试按以下职责组织，每个用例独立创建数据和配置，不需要真实 OneBot 或本地凭据：

| 测试类 | 覆盖内容 |
| --- | --- |
| `BackgroundVerifyServiceTests` | 24 小时和同意率边界、UTC 时间、多条问卷票数隔离、新票和改票后的再次判定、状态与用户组保存、通知内容和去重、数据库保存与通知异常 |
| `ReviewOptionsValidatorTests` | 票数和同意率合法范围、NaN / 无穷值、多个非法字段同时报告 |
| `ReviewOptionsTests` | 缺省配置、真实配置示例和 JSON 绑定、非法值阻止启动、不同区域设置下的小数解析 |
| `SqliteMigrationTests` | 完整导入、失败清理、拒绝覆盖、原数据查询和改票、时间边界、外键级联、事务回滚、字段长度、WAL 和启动迁移检查 |

审核服务测试使用独立的 SQLite 内存数据库、可推进的固定时钟和记录通知及异常的替身。保存失败时验证状态与用户组均未写入、不发送通知，下一轮可重新判定；通知失败时验证结果已保存且异常被记录，后续检查不会重复通知。迁移测试使用磁盘 SQLite，覆盖逐字段导入校验、外键与级联删除、事务回滚、时间排序、API 读取、权限和改票。QQ 消息的实际送达需在测试群另行验证。

参考 [数据库配置](#数据库配置) 和 [配置文件](#配置文件) 配置数据库和 `appsettings` .

构建并运行程序:
```bash
dotnet run --project src/SurveyBackend/SurveyBackend.csproj
```


## 数据库配置

从 v5 版本开始，本项目仅使用 SQLite，运行时不再需要 MySQL 服务。默认数据库为程序（`SurveyBackend.dll` / 可执行文件）同目录的 `data.db`，与启动时的工作目录无关。`Database:Path` 可指定其他路径；相对路径仍以程序目录为基准，环境变量为 `Database__Path`。删除旧配置中的 `ConnectionStrings:DefaultConnection`，否则程序会明确拒绝启动，避免误连新建空库。

新建的空库在首次启动时自动应用 SQLite 迁移；已有数据库有待应用迁移时拒绝启动，需要先停止服务、备份，再显式更新：

```bash
dotnet SurveyBackend.dll --migrate-database /absolute/path/data.db
```

源码开发也可以使用固定版本的 EF 工具，设计时无需 Bot 或本地凭据：

```bash
dotnet tool restore
dotnet ef migrations script --project src/SurveyBackend --output migrations.sql
dotnet ef database update --project src/SurveyBackend --connection "Data Source=/absolute/path/data.db;Foreign Keys=True"
```

请审阅结构变更后再更新已有库。SQLite 迁移使用新的基线，旧 MySQL 迁移不能直接在 SQLite 上执行。

### 从 v4 及以下版本迁移 - 从 MySQL Dump 导入

导入入口仅用于离线转换，不提供 MySQL 运行时支持。停止原服务后导出完整数据，保留原 Dump 和旧数据库备份；不要把 MySQL SQL 直接交给 SQLite 执行。在仓库根目录执行：

```bash
dotnet run --project src/SurveyBackend -c Release -- --import-mysql-dump db_dump.sql data.db
```

发行版可直接运行 `dotnet SurveyBackend.dll --import-mysql-dump db_dump.sql data.db`。命令参数中的文件路径以当前工作目录为基准。

导入器支持本项目提供的 UTF-8 Dump 格式：InnoDB `CREATE TABLE`、显式列名的 `INSERT INTO ... VALUES`（含多行值和字符串转义）、`SET FOREIGN_KEY_CHECKS`。未知 SQL、缺表/字段、重复键、非法 JSON/枚举/时间、孤立外键或不完整 Dump 会导致整个导入失败。其他导出格式请先转换到这一格式；导入器不会执行 Dump 内的任意 SQL。

目标文件必须不存在。数据先写入同目录的临时库，按原字段类型导入，保留 ID、枚举、禁用状态、JSON 原文、时间精度和投票自增序列；所有行逐字段读回核对，并检查外键及 `integrity_check`。通过后合并 WAL，再将临时库重命名为目标文件。失败不会替换已有数据库，源 Dump 始终保留。ASCII ID 使用 `NOCASE`，保持原 MySQL 的大小写不敏感查询；所有时间按现有 UTC 约定读取，不对 Dump 时间做时区偏移。原 MySQL 迁移历史保存在 `__MySqlMigrationsHistory`，SQLite 自己使用 `__EFMigrationsHistory`；`__DataImport` 保存源文件 SHA-256 和导入行数。

导入成功后，部署时将目标数据库复制到程序目录。`dotnet run` 的默认程序目录是 `bin/<配置>/net10.0/`；直接使用根目录数据库时，请通过绝对路径指定 `Database__Path` 或 `--Database:Path`，避免启动一个新的空库。`data.db`、SQLite 辅助文件和 `db_dump.sql` 已从 Git 和 Docker 构建上下文排除，发布也不会自动打包数据库。

### Docker 持久化

镜像以非 root 用户运行，在 `/data` 创建可写数据目录，并声明 `VOLUME /data`；镜像内默认设置 `Database__Path=/data/data.db`。必须显式挂载整个目录，以持久化数据库及 `-wal` / `-shm` 文件，不能只挂载 `data.db`，也不要把 `/app` 整体覆盖。

完整的构建、配置、目录权限、启动及升级步骤见 [从 Docker 中启动](#从-docker-中启动)。已有迁移库必须在首次启动前放入 `storage/data.db`。重建容器时继续挂载同一目录；删除容器不会删除该宿主目录。若使用命名卷，创建并复用同一个卷，先将迁移数据放入卷内，避免使用自动匿名卷而遗失数据位置。

SQLite 启用外键、WAL 和 60 秒锁等待，适合单实例部署。数据目录应放在本机持久磁盘，避免跨主机共享或网络文件系统。备份时先停止服务，再复制数据库及仍存在的辅助文件；在线备份应使用 SQLite Backup API，不能只复制运行中的 `data.db`。不要将数据库或 Dump 提交到 Git。

## 配置文件


从源码运行时，请将 [src/SurveyBackend/appsettings.example.json](/src/SurveyBackend/appsettings.example.json) 复制为同目录下的 `appsettings.json` 并填写配置。使用发行版 / 构建产物时，请在解压目录中复制配置示例并重命名为 `appsettings.json`。

以下是配置文件详解，**请在配置完毕后删除所有注释**或参考仓库内的 [`appsettings.example.json` 示例文件](/src/SurveyBackend/appsettings.example.json)。

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

  // SQLite 文件路径，相对路径以程序所在目录为基准
  "Database": {
    "Path": "data.db"
  },

  // 符合 OneBot v11 标准的 QQ 机器人配置
  // 连接方式为反向ws连接, 即本程序启动ws服务器供 OneBot 协议端连接
  "Bot": {
    "accessToken": "<Your AccessToken>", // ws连接的accessToken, 你可以自己定义
    "wsPort": 21568, // ws服务器端口
    "mainGroupId": "23********1", // 主群号, 更多信息请参考审核流程参照
    "verifyGroupId": "21******59", // 审核群群号, 更多信息请参考审核流程参照
    "adminId": "56******0" // 管理员ID，将自动在users表中设置身份组为 SuperAdmin
  },

  // 常规审核阈值；超时通过规则固定为超过 24 小时、至少 3 票、同意率不少于 2/3
  "Review": {
    "MinimumVotes": 4, // 触发常规审核判定的最少总票数，必须为正整数
    "AgreeRateThreshold": 0.6 // 同意率必须严格大于此值才通过，取值为 0 到 1
  },

  // AI 见解配置
  "LLM": {
    "ModelName": "gpt-5.6-terra", // 使用的 OpenAI 模型名称
    "OpenAIKey": "sk-****************************", // OpenAI API Key
    "OpenAIEndpoint": "https://api.openai.com/v1", // OpenAI API 基础地址
    "SysPromptPath": "sysPrompt.txt", // 系统提示词文件路径
    "ReasoningEffort": "medium", // 推理强度，可选 none、minimal、low、medium、high、xhigh、max
    "UseWebSearch": true // 是否启用联网搜索, 建议与提示词配合, 设为 true 则强制模型调用联网搜索工具, 为 false 则不允许联网搜索。
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

`Review` 配置在启动时绑定并校验：`MinimumVotes` 必须为正整数，`AgreeRateThreshold` 必须为 0 到 1 之间的有限数值（包含端点）。非法值或无法转换为对应类型的值会阻止程序启动；阈值为 `1` 时，常规审核不会通过，但超时通过规则仍有效。省略整个配置节或单个字段时，分别使用默认值 `4` 和 `0.6`，现有配置无需补充字段即可启动。调整后须重启程序；本次变更不需要数据库迁移。

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

#### 未推送/未审核提交检查 | `BackgroundPushingService`

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
  3. 优先检查超时通过条件：提交时间 `Submission.CreatedAt`（UTC）距本轮检查时间**超过 24 小时**，且总投票数 ≥ 3、同意率 ≥ 2/3 时，直接通过。总票数仅统计同意票与拒绝票，恰好 2 票同意、1 票拒绝也满足同意率条件。
  4. 未满足超时通过条件时，按 `Review` 配置进行常规判定：总票数 ≥ `MinimumVotes`（默认 4），且同意率**严格大于** `AgreeRateThreshold`（默认 0.6）则通过；达到票数但同意率未超过阈值则拒绝，票数不足则保持待审核。超时条件本身不会触发拒绝。时长按原提交时间计算，管理员重新设置为 `Pending` 不会重置该时间。判定在下一轮检查时执行，OneBot 不可用或服务暂停时延后。
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

### 测试系统提示词

建议在测试环境中配置 `LLM` 节点和系统提示词文件，并为问卷设置需要分析的页面名称 `LLMPageNames`，然后提交测试问卷，检查生成的 AI 见解。

修改系统提示词后，请重启后端。管理员或超级管理员可以通过机器人指令 `/survey reinsight <SubmissionId>` 为已有的审核提交重新生成见解；生成成功后，新见解会覆盖数据库中该提交的原有见解。

已审核用户、管理员或超级管理员可以使用 `/survey insight <SubmissionId>` 查看已保存的见解。这两个指令均支持使用能够唯一匹配提交的 ID 前缀。

## 许可证

本项目采用 MIT 许可证，详情请参见 `LICENSE.txt` 文件。

Copyright © 2026 [厦门六中同安校区音游部](https://github.com/ltyyb) & [Aunt Studio](https://github.com/Aunt-Studio)
