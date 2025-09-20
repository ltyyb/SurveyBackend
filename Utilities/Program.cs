using System.Text.Json;

namespace Utilities
{
    internal class Program
    {
        public static readonly JsonSerializerOptions surveyPkgJsonOpt = new()
        {
            WriteIndented = false
        };
        private async static Task Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args[0] == "llmtest")
                {
                    Console.WriteLine("=== LLM 问卷审阅测试工具 ===");
                    LLMTools lLMTools = new LLMTools();
                    Console.WriteLine("System:\n" + LLMTools.SysPrompt);
                    Console.WriteLine("\n拖入原始问卷: ");
                    string surveyPath = Console.ReadLine();
                    Console.WriteLine("\n拖入用户回答: ");
                    string responsePath = Console.ReadLine();
                    string surveyJson = File.ReadAllText(surveyPath);
                    string responseJson = File.ReadAllText(responsePath);
                    var surveyPrompt = await lLMTools.ParseSurveyResponseToNL(surveyJson, responseJson);
                    Console.WriteLine("\n用户问卷内容:\n" + surveyPrompt);
                    Console.WriteLine("回车发送给LLM");
                    Console.ReadLine();
                    var result = await lLMTools.GetInsight(surveyPrompt);
                    Console.WriteLine("\nLLM 回复:\n" + result);
                }
                if (args[0] == "packSurvey")
                {
                    Console.WriteLine("=== Survey 打包交互工具 ===");
                    var psjPath = string.Empty;
                    if (args.Length == 2 && File.Exists(args[1]))
                    {
                        psjPath = args[1];
                        bool flowControl = UpdatePkg(psjPath);
                        if (!flowControl)
                        {
                            return;
                        }
                    }
                    else
                    {
                        Console.Write("输入或拖入已有 PSJ 文件 (新建请留空): ");
                        psjPath = Console.ReadLine();
                        if (string.IsNullOrWhiteSpace(psjPath))
                        {
                            CreateNewPSJ();
                        }
                        if (File.Exists(psjPath))
                        {
                            bool flowControl = UpdatePkg(psjPath);
                            if (!flowControl)
                            {
                                return;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"提供的 PSJ 文件不存在: {psjPath}");
                            Console.WriteLine();
                        }
                    }
                    Console.WriteLine("\n");
                }
                else
                {
                    Console.WriteLine("啥参数?\n 试试 packSurvey");
                }
            }
            else
            {
                Console.WriteLine("没有参数。\n 试试 packSurvey");
            }

            Console.WriteLine("按Enter键然后出去。");
            Console.ReadLine();
        }

        private static bool UpdatePkg(string psjPath)
        {
            Console.WriteLine($"已加载 PSJ 文件: {psjPath}\n");
            string jsonString = File.ReadAllText(psjPath);
            try
            {
                var surveyPkg = JsonSerializer.Deserialize<SurveyPackage>(jsonString);
                if (surveyPkg is null
                    || string.IsNullOrWhiteSpace(surveyPkg.Name)
                    || string.IsNullOrWhiteSpace(surveyPkg.LatestVer)
                    || surveyPkg.Surveys.Count < 1)
                {
                    Console.WriteLine("无法解析 PSJ 文件，请检查文件格式。");
                    return false;
                }
                Console.WriteLine($"已加载 Survey 包 {surveyPkg.Name}。");

                Console.WriteLine($"该包具有 {surveyPkg.Surveys.Count} 个版本。");
                Console.WriteLine($"当前最新版本: {surveyPkg.LatestVer}。\n");
                Console.WriteLine("现有版本列表:");
                foreach (var kvp in surveyPkg.Surveys)
                {
                    Console.WriteLine($"""
                                    - {kvp.Key}
                                      | 描述: {kvp.Value.Description}
                                      | 发布日期: {ParseReleaseDate(kvp.Value.ReleaseDate)}
                                    """);
                }


                Console.WriteLine("\n\n=== Survey 更新版本交互 ===\n");
                Console.WriteLine("请输入新版本版本号: ");
                var version = Console.ReadLine();
                while (string.IsNullOrWhiteSpace(version))
                {
                    Console.WriteLine("\n   版本号不能为空，请重新输入。");
                    Console.Write("添加你的第一个版本号: ");
                    version = Console.ReadLine();
                }
            jsonInput: Console.Write("\n输入 Survey Json或直接拖入 Json 文件: ");
                var surveyJson = Console.ReadLine();
                while (string.IsNullOrWhiteSpace(surveyJson))
                {
                    Console.WriteLine("\n   Survey Json 不能为空，请重新输入。");
                    Console.Write("输入 Survey Json 内容或直接拖入 Json 文件: ");
                    surveyJson = Console.ReadLine();
                }
                if (File.Exists(surveyJson))
                {
                    try
                    {
                        var surveyJsonText = File.ReadAllText(surveyJson);
                        if (!IsValidJson(surveyJsonText))
                        {
                            Console.WriteLine("提供的文件内容不是有效的 JSON，请检查文件内容。");
                            goto jsonInput;
                        }
                        surveyJson = surveyJsonText;
                        Console.WriteLine("已从文件中读取 Survey Json。");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"无法读取文件内容, 发生异常: {ex.Message}");
                        goto jsonInput;
                    }
                }
                else
                {
                    if (!IsValidJson(surveyJson))
                    {
                        Console.WriteLine("提供的内容不是有效的 JSON，请检查输入。");
                        goto jsonInput;
                    }
                }
                Console.Write("\n输入版本描述: ");
                var versionDec = Console.ReadLine();
                while (string.IsNullOrWhiteSpace(versionDec))
                {
                    Console.WriteLine("\n   版本描述不能为空，请重新输入。");
                    Console.Write("输入版本描述: ");
                    versionDec = Console.ReadLine();
                }
                var releaseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                surveyPkg.Surveys.Add(version, new SurveyInfo
                {
                    Description = versionDec,
                    ReleaseDate = releaseTime,
                    SurveyJson = surveyJson
                });
                Console.WriteLine("\n 已写入。");
                Console.WriteLine("""
                                是否将本版本设置为最新版本?
                                Y 是 (Default) | N 否 | E 指定
                                """);

                switch (Console.ReadLine()?.ToUpperInvariant())
                {
                    case "Y":
                        surveyPkg.LatestVer = version;
                        Console.WriteLine($"已将最新版本设置为{version}。");
                        break;
                    case "N":
                        Console.WriteLine($"保留当前版本{surveyPkg.LatestVer}。");
                        break;
                    case "E":
                        Console.Write("输入最新版本号: ");
                        var latestVer = Console.ReadLine();
                        while (string.IsNullOrWhiteSpace(latestVer))
                        {
                            Console.WriteLine("\n   版本号不能为空，请重新输入。");
                            Console.Write("输入最新版本号: ");
                            latestVer = Console.ReadLine();
                        }
                        surveyPkg.LatestVer = latestVer;
                        Console.WriteLine($"\n已将最新版本设置为{latestVer}。");
                        break;
                    default:
                        surveyPkg.LatestVer = version;
                        Console.WriteLine($"已将最新版本设置为{version}。");
                        break;
                }
                Console.WriteLine("正在打包 Survey，请稍候...\n");

                string newJsonString = JsonSerializer.Serialize(surveyPkg, surveyPkgJsonOpt);
                File.WriteAllText(psjPath, newJsonString);
                Console.WriteLine($"Survey 包已更新并保存到 {psjPath}。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法写入, 发生异常{ex.Message}");
                Console.WriteLine("\n\n异常: ");
                Console.WriteLine(ex.ToString());
            }

            return true;
        }

        private static void CreateNewPSJ()
        {
            Console.WriteLine("将创建新的打包 Survey。");
            Console.Write("输入 Survey Name: ");
            var name = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(name))
            {
                Console.WriteLine("\n   Survey Name 不能为空，请重新输入。");
                Console.Write("输入 Survey Name: ");
                name = Console.ReadLine();
            }
            Console.Write("\n添加你的第一个版本号: ");
            var version = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(version))
            {
                Console.WriteLine("\n   版本号不能为空，请重新输入。");
                Console.Write("添加你的第一个版本号: ");
                version = Console.ReadLine();
            }

        jsonInput: Console.Write("\n输入 Survey Json或直接拖入 Json 文件: ");
            var surveyJson = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(surveyJson))
            {
                Console.WriteLine("\n   Survey Json 不能为空，请重新输入。");
                Console.Write("输入 Survey Json 内容或直接拖入 Json 文件: ");
                surveyJson = Console.ReadLine();
            }
            if (File.Exists(surveyJson))
            {
                try
                {
                    var surveyJsonText = File.ReadAllText(surveyJson);
                    if (!IsValidJson(surveyJsonText))
                    {
                        Console.WriteLine("提供的文件内容不是有效的 JSON，请检查文件内容。");
                        goto jsonInput;
                    }
                    surveyJson = surveyJsonText;
                    Console.WriteLine("已从文件中读取 Survey Json。");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"无法读取文件内容, 发生异常: {ex.Message}");
                    goto jsonInput;
                }
            }
            else
            {
                if (!IsValidJson(surveyJson))
                {
                    Console.WriteLine("提供的内容不是有效的 JSON，请检查输入。");
                    goto jsonInput;
                }
            }
            Console.Write("\n输入版本描述: ");
            var versionDec = Console.ReadLine();
            while (string.IsNullOrWhiteSpace(versionDec))
            {
                Console.WriteLine("\n   版本描述不能为空，请重新输入。");
                Console.Write("输入版本描述: ");
                versionDec = Console.ReadLine();
            }
            Console.WriteLine($"最新版本号为{version}。");
            var releaseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            Console.WriteLine("正在打包 Survey，请稍候...\n");
            var survey = new SurveyPackage
            {
                Name = name,
                LatestVer = version,
                Surveys = new Dictionary<string, SurveyInfo>
                {
                    [version] = new SurveyInfo
                    {
                        Description = versionDec,
                        ReleaseDate = releaseTime,
                        SurveyJson = surveyJson
                    }
                }
            };
            try
            {
                string jsonString = JsonSerializer.Serialize(survey, surveyPkgJsonOpt);

                File.WriteAllText($"{name}.psj", jsonString);
                Console.WriteLine($"Survey 包已创建并保存为 {name}.psj。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法写入, 发生异常{ex.Message}");
                Console.WriteLine("\n\n异常: ");
                Console.WriteLine(ex.ToString());
            }

        }
        private static bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
        private static string ParseReleaseDate(string releaseDate)
        {
            if (long.TryParse(releaseDate, out long unixTime))
            {
                var time = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
                return time.ToString("yyyy-MM-dd");
            }
            else
            {
                throw new FormatException("Invalid release date format.");
            }
        }
    }
}
