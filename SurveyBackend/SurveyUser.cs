namespace SurveyBackend
{
    public class SurveyUser
    {
        public string UserId { get; set; } = string.Empty;
        public string QQId { get; set; } = string.Empty;
        public SurveyUser()
        {

        }

        /// <summary>
        /// 工厂: 从数据库中通过 <paramref name="userId"/> 获取用户 QQ号，并返回一个 <see cref="SurveyUser"/> 实例。
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public static SurveyUser? GetUserById(string userId)
        {
            if (userId != "qwq") return null;
            // 从数据库中获取 UserId 对应的 QQ号
            string qqId = "123456789"; // 这里应该是实际从数据库查询的结果
            return new SurveyUser { UserId = userId, QQId = qqId };
        }
    }
}
