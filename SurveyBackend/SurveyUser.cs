 using MySqlConnector;
using Sisters.WudiLib.Posts;
using System.Text.RegularExpressions;
using System.Threading;

namespace SurveyBackend
{
    public class SurveyUser
    {
        private static readonly Regex SafeNameRegex = new(@"^[a-zA-Z0-9_]+$");
        public string UserId { get; set; } = string.Empty;
        public string QQId { get; set; } = string.Empty;
        public SurveyUser()
        {

        }
        public SurveyUser(string userId, string qqId)
        {
            UserId = userId;
            QQId = qqId;
        }

        /// <summary>
        /// 工厂: 从数据库中通过 <paramref name="qqId"/> 获取用户 userId ，并返回一个 <see cref="SurveyUser"/> 实例。
        /// </summary>
        /// <param name="qqId"></param>
        /// <param name="logger"></param>
        /// <param name="connStr"></param>
        /// <returns></returns>
        public static async Task<SurveyUser?> GetUserByQQIdAsync(string qqId, ILogger logger, string connStr)
        {
            if (string.IsNullOrWhiteSpace(qqId))
            {
                logger.LogError("GetUserByQQId called with null or empty QQId.");
                return null;
            }
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                const string sql = "SELECT UserId FROM QQUsers WHERE QQId = @qqId LIMIT 1";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@qqId", qqId);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    string userId = result.ToString() ?? string.Empty;
                    logger.LogInformation("Found UserId for QQId {QQId}: {UserId}", qqId, userId);
                    return new SurveyUser(userId, qqId);
                }
                else
                {
                    logger.LogInformation("No UserId found for QQId: {QQId}", qqId);
                    return null;
                }
            }
            catch (MySqlException ex)
            {
                logger.LogError(ex, "Database error in GetUserByQQId for QQId: {QQId}", qqId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in GetUserByQQId for QQId: {QQId}", qqId);
                return null;
            }
        }
        /// <summary>
        /// 工厂: 从数据库中通过 <paramref name="userId"/> 获取用户 QQ号，并返回一个 <see cref="SurveyUser"/> 实例。
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public static async Task<SurveyUser?> GetUserByIdAsync(string userId, ILogger logger, string connStr)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                logger.LogError("GetQQIdByUserIdAsync called with null or empty UserId.");
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
            return new SurveyUser(userId, qqId);
        }

        public static async Task<SurveyUser?> GetUserByRequestIdAsync(string requestId, ILogger logger, string connStr)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                logger.LogError("GetUserByRequestIdAsync called with null or empty RequestId.");
                return null;
            }
            string userId = string.Empty;
            try
            {
                await using (var conn = new MySqlConnection(connStr))
                {
                    await conn.OpenAsync();
                    const string sql = "SELECT UserId FROM RequestId WHERE RequestId = @requestId AND CreateTime > NOW() - INTERVAL 2 HOUR LIMIT 1";
                    await using var cmd = new MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@requestId", requestId);

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        userId = result?.ToString() ?? string.Empty;
                        logger.LogInformation("Found UserId for RequestId {UserId}: {RequestId}", userId, requestId);
                    }
                    else
                    {
                        logger.LogInformation("No available UserId found for RequestId: {UserId}", requestId);
                    }
                }

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    return await GetUserByIdAsync(userId, logger, connStr);
                }
                else
                {
                    return null;
                }
            }
            catch (MySqlException ex)
            {
                logger.LogError(ex, "Database error in GetUserByRequestIdAsync for RequestId: {RequestId}", requestId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in GetUserByRequestIdAsync for RequestId: {RequestId}", requestId);
                return null;
            }
        }

        /// <summary>
        /// <para>
        /// 传入 QQId，随机生成 UserId，并返回一个 <see cref="SurveyUser"/> 实例。
        /// </para>
        /// <para>
        /// 注意，此方法不会自动将用户注册入数据库中。获得的 <see cref="SurveyUser"/> 实例需要调用 <seealso cref="RegisterAsync(ILogger, string)"/> 方法来保存到数据库。
        /// </para>
        /// </summary>
        /// <param name="qqId"></param>
        /// <returns></returns>
        public static async Task<SurveyUser?> CreateUserByQQId(string qqId, string connStr)
        {
            var userId = Guid.NewGuid().ToString("N")[..30]; // 生成一个随机的 UserId
            while (await CheckValueExistsAsync(connStr, "qqusers", "UserId", userId))
            {
                userId = Guid.NewGuid().ToString("N")[..30];
            }

            return new SurveyUser
            {
                QQId = qqId,
                UserId = userId
            };
        }


        private static async Task<bool> CheckValueExistsAsync(string connectionString, string tableName, string columnName, object value)
        {
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

        public async Task<bool> RegisterAsync(ILogger logger, string connStr)
        {
            if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrWhiteSpace(QQId))
            {
                logger.LogWarning("SaveAsync called with null or empty UserId or QQId.");
                return false;
            }
            try
            {
                if (await IsUserExisted(connStr))
                {
                    return false;
                }
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

        /// <summary>
        /// 检查该用户对应QQ号是否已经在数据库中
        /// </summary>
        /// <param name="connStr"></param>
        /// <returns></returns>
        public async Task<bool> IsUserExisted(string connStr)
        {
            return await CheckValueExistsAsync(connStr, "QQUsers", "QQId", QQId);
        }

        /// <summary>
        /// 获取与该用户绑定的临时 RequestId。如果仍有 RequestId 在一个小时以上时间有效，则返回已存在的 RequestId。
        /// 否则重新生成并记录到数据库。
        /// </summary>
        /// <param name="connStr"></param>
        /// <returns></returns>
        public async Task<string?> GetTempRequestId(ILogger logger, string connStr)
        {

            try
            {
                if (!await IsUserExisted(connStr))
                {
                    logger.LogError("无法为未注册的用户创建 RequestId.");
                    return null;
                }
                // 查询是否有一个小时内创建的 RequestId
                List<(string, DateTime)> recentRequestId = new();
                await using (var conn = new MySqlConnection(connStr))
                {
                    await conn.OpenAsync();
                    const string sql = "SELECT RequestId, CreateTime FROM `requestid` WHERE UserId = @userId AND CreateTime > NOW() - INTERVAL 1 HOUR";
                    await using var cmd = new MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@userId", UserId);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        recentRequestId.Add((reader.GetString("RequestId"), (reader.GetDateTime("CreateTime"))));
                    }
                }

                // 如果存在返回最新的
                if (recentRequestId.Count > 0)
                {
                    var latest = recentRequestId.MaxBy(r => r.Item2);
                    logger.LogInformation("Found latest existing RequestId for UserId {UserId}: {RequestId}", UserId, latest.Item1);
                    return latest.Item1;
                }
                else
                {
                    // 否则生成新的 RequestId 并保存
                    string? newRequestId = await GenerateRequestId(logger, connStr);
                    if (string.IsNullOrEmpty(newRequestId))
                    {
                        logger.LogError("Failed to request to generate new RequestId for UserId {UserId}", UserId);
                        return null;
                    }
                    return newRequestId;
                }

            }
            catch (MySqlException ex)
            {
                logger.LogError(ex, "Database error while saving SurveyUser: {UserId} ({QQId})", UserId, QQId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while saving SurveyUser: {UserId} ({QQId})", UserId, QQId);
                return null;
            }

        }

        /// <summary>
        /// 生成新的 RequestId 并提交到数据库
        /// </summary>
        /// <param name="connStr"></param>
        /// <returns></returns>
        private async Task<string?> GenerateRequestId(ILogger logger, string connStr)
        {
            string newRequestId = Guid.NewGuid().ToString("N")[..30]; // 生成一个随机的 RequestId
            await using (var conn = new MySqlConnection(connStr))
            {
                await conn.OpenAsync();
                const string insertSql = "INSERT INTO `requestid` (RequestId, UserId) VALUES (@requestId, @userId)";
                await using var cmd = new MySqlCommand(insertSql, conn);
                cmd.Parameters.AddWithValue("@requestId", newRequestId);
                cmd.Parameters.AddWithValue("@userId", UserId);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    logger.LogInformation("Generated and saved new RequestId for UserId {UserId}: {RequestId}", UserId, newRequestId);
                    return newRequestId;
                }
                else
                {
                    logger.LogError("Failed to save new RequestId for UserId {UserId}: {RequestId}", UserId, newRequestId);
                    return null;
                }
            }
        }
        public static async Task<bool> IsUserExisted(string qqId, string connStr)
        {
            var surveyUser = new SurveyUser
            {
                QQId = qqId
            };
            return await surveyUser.IsUserExisted(connStr);
        }

    }
}
