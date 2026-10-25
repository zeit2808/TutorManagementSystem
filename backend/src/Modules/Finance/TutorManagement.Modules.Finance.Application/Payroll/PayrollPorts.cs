using TutorManagement.Modules.Finance.Domain.Payroll;

namespace TutorManagement.Modules.Finance.Application.Payroll;

public sealed record PayrollTeacherRow(Guid Id, string Code, string FullName, Guid? UserId, bool IsArchived);

public sealed record PayrollClassRow(Guid Id, string Code, string Name);

/// <summary>
/// A session with what payroll needs: who was assigned, whether it took place, and — when its attendance sheet is
/// submitted — who actually taught (C5).
/// </summary>
public sealed record PayrollSessionRow(Guid Id, Guid ClassId, Guid TeacherId, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    bool IsActive, bool Submitted, Guid? ActualTeacherId, Guid? MakeupForSessionId)
{
    public int Minutes => (int)Math.Round((EndUtc - StartUtc).TotalMinutes);
}

public sealed record PayrollTeacherProfile(Guid CenterId, Guid TeacherId);

public sealed record PayrollCenterRow(Guid Id, string DisplayName, string Status);

/// <summary>Read-only SQL over Academics / Organizations, on the finance connection.</summary>
public interface IPayrollAcademicsReader
{
    Task<IReadOnlyList<PayrollTeacherRow>> ListTeachersAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollClassRow>> ListClassesAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollSessionRow>> ListSessionsAsync(Guid centerId, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        CancellationToken token);
    /// <summary>Teacher profiles linked to a login in approved centers.</summary>
    Task<IReadOnlyList<PayrollTeacherProfile>> ListTeacherProfilesOfUserAsync(Guid userId, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, string>> ReadCenterNamesAsync(IReadOnlyCollection<Guid> centerIds, CancellationToken token);
    Task<bool> CenterExistsAsync(Guid centerId, CancellationToken token);
    /// <summary>Approved and suspended centers: the ones payroll can be configured for.</summary>
    Task<IReadOnlyList<PayrollCenterRow>> ListOperatingCentersAsync(CancellationToken token);
}

public interface IPayrollStore
{
    Task<PayrollConfig?> FindConfigAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollConfigChange>> ListConfigChangesAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollConfigChange>> ListAllConfigChangesAsync(CancellationToken token);
    Task<IReadOnlyDictionary<Guid, DateOnly>> LatestClosedMonthsAsync(CancellationToken token);
    Task<IReadOnlyList<PayRate>> ListRatesAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollAdjustment>> ListAdjustmentsAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<SharedExpense>> ListExpensesAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollPayout>> ListPayoutsAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollDispute>> ListDisputesAsync(Guid centerId, CancellationToken token);
    /// <summary>With lines.</summary>
    Task<IReadOnlyList<PayrollClosedMonth>> ListClosedMonthsAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollCarry>> ListCarriesAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<PayrollMonthEvent>> ListEventsAsync(Guid centerId, CancellationToken token);
    Task<int> NextPayoutNumberAsync(Guid centerId, CancellationToken token);

    Task AddAsync<T>(T entity, CancellationToken token) where T : class;
    void Remove<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken token);
}
