namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>Per-center tuition settings: the bank account shown as a VietQR code (H10) and the due window (H15).</summary>
public sealed class FinanceSettings
{
    public const int DefaultDueDays = 7;
    public const int MaxDueDays = 60;

    private FinanceSettings() { }

    public Guid CenterId { get; private set; }
    /// <summary>NAPAS bank BIN, e.g. 970436 for Vietcombank.</summary>
    public string? BankBin { get; private set; }
    public string? BankAccountNumber { get; private set; }
    public string? BankAccountName { get; private set; }
    public int DueDays { get; private set; } = DefaultDueDays;
    public bool EmailReminders { get; private set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public int Version { get; private set; }

    public bool HasBankAccount => BankBin is not null && BankAccountNumber is not null && BankAccountName is not null;

    public static FinanceSettings Default(Guid centerId) => new() { CenterId = centerId };

    public static bool IsValid(string? bankBin, string? accountNumber, string? accountName, int dueDays)
    {
        var any = !string.IsNullOrWhiteSpace(bankBin) || !string.IsNullOrWhiteSpace(accountNumber) ||
            !string.IsNullOrWhiteSpace(accountName);
        var all = !string.IsNullOrWhiteSpace(bankBin) && !string.IsNullOrWhiteSpace(accountNumber) &&
            !string.IsNullOrWhiteSpace(accountName);
        if (any && !all) return false;
        if (all && (bankBin!.Trim().Length != 6 || !bankBin.Trim().All(char.IsAsciiDigit) ||
            accountNumber!.Trim().Length is < 4 or > 20 || !accountNumber.Trim().All(char.IsAsciiLetterOrDigit) ||
            accountName!.Trim().Length is < 2 or > 100)) return false;
        return dueDays is >= 0 and <= MaxDueDays;
    }

    public void Update(string? bankBin, string? accountNumber, string? accountName, int dueDays, bool emailReminders,
        Guid actorId, DateTimeOffset now)
    {
        if (!IsValid(bankBin, accountNumber, accountName, dueDays)) throw new ArgumentException("Settings are invalid.");
        BankBin = Blank(bankBin);
        BankAccountNumber = Blank(accountNumber);
        BankAccountName = Blank(accountName)?.ToUpperInvariant();
        DueDays = dueDays;
        EmailReminders = emailReminders;
        UpdatedAtUtc = now;
        UpdatedByUserId = actorId;
        Version++;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
