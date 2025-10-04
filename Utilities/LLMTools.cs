using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using OpenAI;

namespace Utilities
{
    public class LLMTools
    {
        public const string SysPrompt = """
                你是一个AI问卷审阅者。你正在审阅厦门六中同安校区音游部的新生入群问卷。
                本音游部是一个包容性较强的社群，不必过多考虑问卷填写者的音游相关实力。
                但我们希望创造一个和谐的讨论氛围并尽量隔离成绩造假行为的出现。所以我们使用了这一问卷审核制度。

                你现在需要根据以下要点，给出问卷评分及相关见解：
                1. 评分范围为0-100分，如无明显问题的问卷不应评定低于75分。
                2. 提供的问卷可能为自然语言形式，我将提供给你 Part 2 - Part 3 的问题以及用户的填写。Part 2 与 3 的作答可能为混合提供。
                3. Part 2 的填写内容均为音游素养相关的问题，请注意该部分的审核，不必过多考虑问卷填写者的音游相关实力，
                    但如果发现其填写内容中有明显的前后题目选择不一致或可能存在作假或虚填行为，请酌情扣分并在见解中指出。
                    例如，某个用户填写了擅长/喜爱的音乐游戏玩法种类，但在勾选其曾经/现在接触过的音乐游戏却没有该种玩法，且该情况多次出现，应考虑扣分。
                    请注意，用户不一定填写自己的音游水平量化值，表现为某个音游PTT/RKS/Level 为 0. 请勿以此为评分依据。
                    此部分评分重点在于用户的作答是否合理、前后是否一致、是否有明显的作假嫌疑，不建议以参与度低作为扣分理由。
                    请注意，除了"请在下方填写您其它音游的潜力值或其他能代表您该游戏水平的指标"题目(如果有)以外，该部分表述均为预设的CheckBox题，即回答表述均为预设，请勿以规范表达为由扣分。
                4. Part 3 的填写内容为成员素质保证测试，如在其中可能有违反社群规定和NSFW内容倾向的选择，请酌情扣分并在见解中指出。
                5. 你需要在回答的开头直接点明你的分数，并在见解中简单总结该用户的填写情况，在这之后指出扣分的原因。
                6. 你的回答不应该为 Markdown 格式，善用换行符。

                稍后 user 将提供问卷内容，请你根据上述要求进行评分和见解分析。
                """;
        private List<ChatMessage> _chatHistory = [];
        private readonly IChatClient chatClient;
        public LLMTools()
        {
            var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
            string model = config["ModelName"];
            string key = config["OpenAIKey"];
            string endpoint = config["OpenAIEndpoint"];

            // Create the IChatClient
            chatClient =
                new OpenAIClient(new System.ClientModel.ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(model).AsIChatClient();
        }

        public async Task<string> GetInsight(string surveyContentPrompt)
        {

            _chatHistory =
            [
                new ChatMessage(ChatRole.System, SysPrompt)
            ];
            string response = string.Empty;
            _chatHistory.Add(new ChatMessage(ChatRole.User, surveyContentPrompt));
            await foreach (ChatResponseUpdate item in
                chatClient.GetStreamingResponseAsync(_chatHistory))
            {
                response += item.Text;
            }
            _chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
            return response;
        }

        public async Task<string> ParseSurveyResponseToNL(string surveyRawJson, string responseRawJson)
        {
            string result = "";
            // 读取问卷结构
            var surveyJson = JObject.Parse(surveyRawJson);

            // 读取用户结果
            var answerJson = JObject.Parse(responseRawJson);

            // 仅取页面 2 和 3 的题目
            var elementDict = surveyJson["pages"]
                .Where(p => p["name"]?.ToString() == "2" || p["name"]?.ToString() == "3")
                .SelectMany(p => p["elements"])
                .ToDictionary(e => e["name"]?.ToString(), e => (JObject)e);

            foreach (var prop in answerJson.Properties())
            {
                string name = prop.Name;
                var value = prop.Value;

                if (!elementDict.TryGetValue(name, out JObject element))
                    continue; // 不在页面2、3里，跳过

                string title = element["title"]?["zh-cn"]?.ToString() ?? name;
                string resultText = "";

                switch (element["type"]?.ToString())
                {
                    case "radiogroup":
                    case "checkbox":
                        var choices = element["choices"] as JArray;
                        if (value.Type == JTokenType.Array)
                        {
                            var texts = new List<string>();
                            foreach (var v in value)
                            {
                                var match = choices?.FirstOrDefault(c => c["value"]?.ToString() == v.ToString());
                                texts.Add(match?["text"]?["zh-cn"]?.ToString() ?? v.ToString());
                            }
                            resultText = string.Join("，", texts);
                        }
                        else
                        {
                            var match = choices?.FirstOrDefault(c => c["value"]?.ToString() == value.ToString());
                            resultText = match?["text"]?["zh-cn"]?.ToString() ?? value.ToString();
                        }
                        break;

                    case "boolean":
                        resultText = (value.Type == JTokenType.Boolean && value.Value<bool>()) ? "是" : "否";
                        break;

                    case "text":
                    case "comment":
                    default:
                        resultText = value.ToString();
                        break;
                }

                result += $"{title}: {resultText}\n";
            }
            return result;
        }
    }
}
