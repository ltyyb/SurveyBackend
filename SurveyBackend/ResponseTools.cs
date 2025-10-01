using MySqlConnector;
using System.ComponentModel;
using System.Text;
using System.Threading;

namespace SurveyBackend
{
    public static class ResponseTools
    {
        public static async Task<bool> HardDeleteResponse(string responseId, ILogger logger, string connStr)
        {
            try
            {
                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                string deleteQuery = "DELETE FROM EntranceSurveyResponses WHERE ResponseId = @responseId";
                using var cmd = new MySqlCommand(deleteQuery, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);
                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    logger.LogInformation($"Response with ID {responseId} deleted successfully.");
                    return true;
                }
                else
                {
                    logger.LogWarning($"No response found with ID {responseId} to delete.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error deleting response with ID {responseId}: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> SoftDeleteResponse(string responseId, ILogger logger, string connStr)
        {
            try
            {
                using (var conn = new MySqlConnection(connStr))
                {
                    await conn.OpenAsync();
                    string originalResponseQuery = "SELECT * FROM EntranceSurveyResponses WHERE ResponseId = @responseId";
                    using var originalResponseCmd = new MySqlCommand(originalResponseQuery, conn);
                    originalResponseCmd.Parameters.AddWithValue("@responseId", responseId);
                    await using var reader = await originalResponseCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        string insertQuery = "INSERT INTO DeletedEntrSurveyResponses (ResponseId, UserId, QQId, ShortId, SurveyVersion, SurveyAnswer, IsPushed, IsReviewed, CreatedAt) " +
                                             "VALUES (@ResponseId, @UserId, @QQId, @ShortId, @SurveyVersion, @SurveyAnswer, @IsPushed, @IsReviewed, @CreatedAt)";
                        using var insertConn = new MySqlConnection(connStr);
                        await insertConn.OpenAsync();
                        using var insertCmd = new MySqlCommand(insertQuery, insertConn);
                        insertCmd.Parameters.AddWithValue("@ResponseId", reader.GetString("ResponseId"));
                        insertCmd.Parameters.AddWithValue("@UserId", reader.GetString("UserId"));
                        insertCmd.Parameters.AddWithValue("@QQId", reader.GetString("QQId"));
                        insertCmd.Parameters.AddWithValue("@ShortId", reader.GetString("ShortId"));
                        insertCmd.Parameters.AddWithValue("@SurveyVersion", reader.GetString("SurveyVersion"));
                        insertCmd.Parameters.AddWithValue("@SurveyAnswer", reader.GetString("SurveyAnswer"));
                        insertCmd.Parameters.AddWithValue("@IsPushed", reader.GetBoolean("IsPushed"));
                        insertCmd.Parameters.AddWithValue("@IsReviewed", reader.GetBoolean("IsReviewed"));
                        insertCmd.Parameters.AddWithValue("@CreatedAt", reader.GetDateTime("CreatedAt"));
                        
                        int insertResult = await insertCmd.ExecuteNonQueryAsync();
                        if (insertResult > 0)
                        {
                            return await HardDeleteResponse(responseId, logger, connStr);
                            
                        }
                        else
                        {
                            logger.LogWarning($"Failed to archive response with ID {responseId}.");
                            return false;
                        }
                    }
                    else
                    {
                        logger.LogWarning($"No response found with ID {responseId} to soft-delete.");
                        return false;
                    }
                }

            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error soft-deleting response with ID {responseId}: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> RestoreResponse(string responseId, ILogger logger, string connStr)
        {
            try
            {
                using (var conn = new MySqlConnection(connStr))
                {
                    await conn.OpenAsync();
                    string originalResponseQuery = "SELECT * FROM DeletedEntrSurveyResponses WHERE ResponseId = @responseId";
                    using var originalResponseCmd = new MySqlCommand(originalResponseQuery, conn);
                    originalResponseCmd.Parameters.AddWithValue("@responseId", responseId);
                    using var reader = await originalResponseCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        using var insertConn = new MySqlConnection(connStr);
                        await insertConn.OpenAsync();
                        string insertQuery = "INSERT INTO EntranceSurveyResponses (ResponseId, UserId, QQId, ShortId, SurveyVersion, SurveyAnswer, IsPushed, IsReviewed, CreatedAt) " +
                                             "VALUES (@ResponseId, @UserId, @QQId, @ShortId, @SurveyVersion, @SurveyAnswer, @IsPushed, @IsReviewed, @CreatedAt)";
                        using var insertCmd = new MySqlCommand(insertQuery, insertConn);
                        insertCmd.Parameters.AddWithValue("@ResponseId", reader.GetString("ResponseId"));
                        insertCmd.Parameters.AddWithValue("@UserId", reader.GetString("UserId"));
                        insertCmd.Parameters.AddWithValue("@QQId", reader.GetString("QQId"));
                        insertCmd.Parameters.AddWithValue("@ShortId", reader.GetString("ShortId"));
                        insertCmd.Parameters.AddWithValue("@SurveyVersion", reader.GetString("SurveyVersion"));
                        insertCmd.Parameters.AddWithValue("@SurveyAnswer", reader.GetString("SurveyAnswer"));
                        insertCmd.Parameters.AddWithValue("@IsPushed", reader.GetBoolean("IsPushed"));
                        insertCmd.Parameters.AddWithValue("@IsReviewed", reader.GetBoolean("IsReviewed"));
                        insertCmd.Parameters.AddWithValue("@CreatedAt", reader.GetDateTime("CreatedAt"));
                        int insertResult = await insertCmd.ExecuteNonQueryAsync();
                        if (insertResult > 0)
                        {
                            // 删除原有归档记录
                            using var deleteConn = new MySqlConnection(connStr);
                            await deleteConn.OpenAsync();
                            string deleteQuery = "DELETE FROM DeletedEntrSurveyResponses WHERE ResponseId = @responseId";
                            using var deleteCmd = new MySqlCommand(deleteQuery, deleteConn);
                            deleteCmd.Parameters.AddWithValue("@responseId", responseId);
                            int deleteResult = await deleteCmd.ExecuteNonQueryAsync();
                            if (deleteResult > 0)
                            {
                                logger.LogInformation($"Response with ID {responseId} restored successfully.");
                            }
                            else
                            {
                                logger.LogWarning($"Failed to delete archived response with ID {responseId} after restoration.");
                            }


                            return true;
                        }
                        else
                        {
                            logger.LogWarning($"Failed to restore response with ID {responseId}.");
                            return false;
                        }
                    }
                    else
                    {
                        logger.LogWarning($"No archived response found with ID {responseId} to restore.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error restoring response with ID {responseId}: {ex.Message}");
                return false;
            }

        }

        public static async Task<string?> GetFullResponseIdAsync(string shortId, ILogger logger, string connStr, bool isDeleted = false)
        {
            if (shortId.Length > 10) return shortId;

            string fullId;

            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            string shortQuery;
            if (isDeleted)
            {
                shortQuery = "SELECT ResponseId FROM DeletedEntrSurveyResponses WHERE ShortId = @shortId LIMIT 1";
            }
            else
            {
                shortQuery = "SELECT ResponseId FROM EntranceSurveyResponses WHERE ShortId = @shortId LIMIT 1";
            }



                await using var cmd = new MySqlCommand(shortQuery, conn);
            cmd.Parameters.AddWithValue("@shortId", shortId);
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                fullId = result.ToString() ?? string.Empty;
                logger.LogInformation("Found full ResponseId for ShortId {short}: {full}", shortId, fullId);
                return fullId;
            }
            else
            {
                logger.LogInformation("Cannot find full ResponseId for {short}", shortId);
                return null;
            }

        }


        public static async Task<string?> GetResponseIdOfQQId(string qqId, ILogger logger, string connStr)
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            const string query = "SELECT ResponseId FROM EntranceSurveyResponses WHERE QQId = @qqId LIMIT 1";
            await using var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@qqId", qqId);
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                string responseId = result.ToString() ?? string.Empty;
                logger.LogInformation("Found ResponseId for QQId {qq}: {responseId}", qqId, responseId);
                return responseId;
            }
            else
            {
                logger.LogInformation("Cannot find ResponseId for QQId {qq}", qqId);
                return null;
            }
        }
        public static async Task<bool> DisableResponse(string responseId, ILogger logger, string connStr)
        {
            try
            {
                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                string updateQuery = "UPDATE EntranceSurveyResponses SET IsDisabled = TRUE WHERE ResponseId = @responseId";
                using var cmd = new MySqlCommand(updateQuery, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);
                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    logger.LogInformation($"Response with ID {responseId} disabled successfully.");
                    return true;
                }
                else
                {
                    logger.LogWarning($"No response found with ID {responseId} to disable.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error disabling response with ID {responseId}: {ex.Message}");
                return false;
            }
        }
        public static async Task<bool> IsResponseDisabled(string responseId, ILogger logger, string connStr)
        {
            try
            {
                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                string query = "SELECT IsDisabled FROM EntranceSurveyResponses WHERE ResponseId = @responseId";
                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@responseId", responseId);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    bool isDisabled = Convert.ToBoolean(result);
                    logger.LogInformation($"Response with ID {responseId} is {(isDisabled ? "disabled" : "enabled")}.");
                    return isDisabled;
                }
                else
                {
                    logger.LogWarning($"No response found with ID {responseId}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error checking if response with ID {responseId} is disabled: {ex.Message}");
                return false;
            }
        }
        public static async Task<List<(string responseId, string qqId, string userId)>?> GetUnreviewedResponseList(ILogger logger, string connStr)
        {
            try
            {
                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                string query = "SELECT ResponseId, QQId, UserId FROM EntranceSurveyResponses WHERE IsReviewed = FALSE";
                using var cmd = new MySqlCommand(query, conn);
                using var reader = await cmd.ExecuteReaderAsync();
                var responseIds = new List<(string responseId, string qqId, string userId)>();
                while (await reader.ReadAsync())
                {
                    responseIds.Add((reader.GetString("ResponseId"), reader.GetString("QQId"), reader.GetString("UserId")));
                }
                logger.LogInformation($"Found {responseIds.Count} unreviewed responses.");
                return responseIds;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error retrieving unreviewed responses: {ex.Message}");
                return null;
            }
        }
    }
}
