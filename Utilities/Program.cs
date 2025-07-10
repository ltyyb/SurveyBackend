namespace Utilities
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args[0] == "-p")
                {
                    if (args.Length != 3 && args.Length != 4)
                    {
                        Console.WriteLine($"参数数量{args.Length}不对。\n  Usage: Utilities -p <version> <surveyJsonFilePath> [outputFilePath]");
                    }
                    else
                    {
                        if (!File.Exists(args[2]))
                        {
                            Console.WriteLine($"文件 \"{args[2]}\" 不存在。好好查查, 大不了用绝对路径。");
                        }
                        else
                        {
                            string version = args[1];
                            // 若没填写第四参数，默认输出到可执行程序旁边的"packed_survey.json"
                            string outputPath = args.Length == 4 ? args[3] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packed_survey.json");
                            string surveyJson = File.ReadAllText(args[2]);
                            string packedJson = PackSurvey(version, surveyJson);

                            File.WriteAllText(outputPath, packedJson);
                            Console.WriteLine($"好了。\n  输出到: {outputPath}");

                        }
                    }
                }
                else
                {
                    Console.WriteLine("啥参数?\n 试试-p");
                }
            }
            else
            {
                Console.WriteLine("没有参数。\n 试试-p");
            }

            Console.WriteLine("按Enter键然后出去。");
            Console.ReadLine();
        }

        private static string PackSurvey(string version, string surveyJson)
        {
            string packedJson = PackedSurveyJsonGenerator.GeneratePackedSurveyJson(version, surveyJson);
            return packedJson;
        }
    }
}
