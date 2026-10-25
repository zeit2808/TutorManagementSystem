namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>Money and calendar rules shared by tuition (decisions H4, H7, H16 in docs/HOC-PHI.md).</summary>
public static class TuitionMath
{
    /// <summary>Largest monthly fee a class may carry (100 million VND).</summary>
    public const long MaxFee = 100_000_000;

    /// <summary>H16: amounts are VND rounded to the nearest 1,000 (half away from zero).</summary>
    public static long RoundToThousand(decimal value) =>
        (long)Math.Round(value / 1000m, MidpointRounding.AwayFromZero) * 1000;

    /// <summary>fee × part ÷ whole, rounded to 1,000đ. A whole of zero gives zero.</summary>
    public static long Prorate(long fee, int part, int whole)
    {
        if (fee < 0 || part < 0 || whole < 0) throw new ArgumentOutOfRangeException(nameof(fee));
        if (whole == 0 || part == 0) return 0;
        if (part >= whole) return fee;
        return RoundToThousand(fee * (decimal)part / whole);
    }

    /// <summary>
    /// H4: cycle <paramref name="index"/> starts <paramref name="index"/> months after the class's first session.
    /// Counting from the anchor each time keeps the 31st on the 31st where the month has one (31/1 → 28/2 → 31/3).
    /// </summary>
    public static DateOnly CycleStart(DateOnly anchor, int index) => anchor.AddMonths(index);

    /// <summary>Last day (inclusive) of cycle <paramref name="index"/>.</summary>
    public static DateOnly CycleEnd(DateOnly anchor, int index) => anchor.AddMonths(index + 1).AddDays(-1);

    /// <summary>The cycle that contains <paramref name="day"/>, or -1 before the anchor.</summary>
    public static int CycleIndexOf(DateOnly anchor, DateOnly day)
    {
        if (day < anchor) return -1;
        var index = (day.Year - anchor.Year) * 12 + day.Month - anchor.Month;
        while (index > 0 && CycleStart(anchor, index) > day) index--;
        while (CycleStart(anchor, index + 1) <= day) index++;
        return index;
    }

    public static bool IsValidFee(long fee) => fee is >= 0 and <= MaxFee && fee % 1000 == 0;
}
