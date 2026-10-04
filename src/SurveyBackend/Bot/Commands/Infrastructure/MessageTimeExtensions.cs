namespace SurveyBackend.Bot.Commands.Infrastructure;

internal static class MessageTimeExtensions
{
    public static DateTime ToDisplayTime(this DateTime utcTime)
    {
        var timeZoneId = Environment.GetEnvironmentVariable("TZ");
        var timeZone = string.IsNullOrWhiteSpace(timeZoneId)
            ? TimeZoneInfo.Local
            : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
    }
}
