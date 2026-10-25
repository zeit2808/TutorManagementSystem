using System.Globalization;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>Decision D12: every center runs on Vietnam time.</summary>
public static class CenterTime
{
    public const string ZoneId = "Asia/Ho_Chi_Minh";

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    /// <summary>The first instant (UTC) of the local day after the one containing <paramref name="utc"/>.</summary>
    public static DateTimeOffset EndOfLocalDay(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Zone);
        var nextMidnight = local.Date.AddDays(1);
        return new DateTimeOffset(nextMidnight, Zone.GetUtcOffset(nextMidnight)).ToUniversalTime();
    }

    private static readonly string[] Weekdays = ["Chủ nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];

    /// <summary>"Thứ Ba 28/10/2026 18:00–19:30" on the Vietnam clock, for notices and emails.</summary>
    public static string FormatSlot(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var start = TimeZoneInfo.ConvertTime(startUtc, Zone);
        var end = TimeZoneInfo.ConvertTime(endUtc, Zone);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Weekdays[(int)start.DayOfWeek]} {start:dd/MM/yyyy} {start:HH:mm}–{end:HH:mm}");
    }

    /// <summary>"Thứ Ba 28/10/2026 18:00" on the Vietnam clock.</summary>
    public static string FormatStart(DateTimeOffset startUtc)
    {
        var start = TimeZoneInfo.ConvertTime(startUtc, Zone);
        return string.Create(CultureInfo.InvariantCulture, $"{Weekdays[(int)start.DayOfWeek]} {start:dd/MM/yyyy} {start:HH:mm}");
    }
}
