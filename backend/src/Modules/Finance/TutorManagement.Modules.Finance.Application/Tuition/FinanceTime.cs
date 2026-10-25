namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>D12: every center runs on Vietnam time; tuition cycles are calendar days on that clock.</summary>
public static class FinanceTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public static DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).Date);
}
