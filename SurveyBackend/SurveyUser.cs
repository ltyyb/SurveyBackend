using Microsoft.Extensions.Configuration;
using MySqlConnector;

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
        public async static Task<SurveyUser?> GetUserByIdAsync(string userId, ILogger logger, string connStr)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                logger.LogWarning("GetQQIdByUserIdAsync called with null or empty UserId.");
                return null;
            }
            string qqId = string.Empty;
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                const string sql = "SELECT QQId FROM QQUsers WHERE UserId = @userId LIMIT 1";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@userId", userId);

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                {
                    qqId = result?.ToString() ?? string.Empty;
                    logger.LogInformation("Found QQId for UserId {UserId}: {QQId}", userId, qqId);
                }
                else
                {
                    logger.LogInformation("No QQId found for UserId: {UserId}", userId);
                }
            }
            catch (MySqlException ex)
            {
                logger.LogError(ex, "Database error in GetQQIdByUserIdAsync for UserId: {UserId}", userId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in GetQQIdByUserIdAsync for UserId: {UserId}", userId);
                return null;
            }
            if (string.IsNullOrEmpty(qqId))
            {
                logger.LogWarning("QQId is null or empty for UserId: {UserId}", userId);
                return null;
            }
            return new SurveyUser { UserId = userId, QQId = qqId };
        }

        public async Task<bool> RegisterAsync(ILogger logger, string connStr)
        {
            if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrWhiteSpace(QQId))
            {
                logger.LogWarning("SaveAsync called with null or empty UserId or QQId.");
                return false;
            }
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                const string sql = "INSERT INTO QQUsers (UserId, QQId) VALUES (@userId, @qqId) ON DUPLICATE KEY UPDATE QQId = @qqId";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@userId", UserId);
                cmd.Parameters.AddWithValue("@qqId", QQId);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    logger.LogInformation("Saved SurveyUser successfully: {UserId} ({QQId})", UserId, QQId);
                    return true;
                }
                else
                {
                    logger.LogWarning("No rows affected when saving SurveyUser: {UserId} ({QQId})", UserId, QQId);
                    return false;
                }
            }
            catch (MySqlException ex)
            {
                logger.LogError(ex, "Database error while saving SurveyUser: {UserId} ({QQId})", UserId, QQId);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while saving SurveyUser: {UserId} ({QQId})", UserId, QQId);
                return false;
            }
        }

    }
}
