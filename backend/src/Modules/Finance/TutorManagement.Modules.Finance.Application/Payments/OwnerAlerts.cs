using System.Globalization;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>
/// Changes that decide where money goes (D7): every active owner is emailed, in the caller's transaction,
/// so a stolen session cannot redirect tuition quietly.
/// </summary>
public static class OwnerAlerts
{
    public static async Task QueueAsync(ITuitionStore store, ICenterMemberDirectory members, Guid centerId, Guid actorId,
        string subject, string what, IReadOnlyList<string> changes, string ifNotYou, DateTimeOffset now, CancellationToken token)
    {
        var owners = await members.ListActiveUserIdsAsync(centerId, ["CenterOwner"], token);
        if (owners.Count == 0) return;
        var emails = await store.ReadEmailsAsync([.. owners, actorId], token);
        var actor = emails.TryGetValue(actorId, out var actorEmail) ? actorEmail : actorId.ToString();
        var at = TimeZoneInfo.ConvertTime(now, FinanceTime.Zone).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
        var body = $"""
            {what}

            Thời điểm: {at} (giờ Việt Nam)
            Người thay đổi: {actor}
            Nội dung:
            {string.Join('\n', changes.Select(x => "- " + x))}

            Nếu bạn không thực hiện thay đổi này: {ifNotYou}
            """;
        var outgoing = owners.Where(emails.ContainsKey).Select(x => new OutgoingTuitionEmail(emails[x], subject, body)).ToArray();
        await store.QueueEmailsAsync(outgoing, token);
    }

    /// <summary>Maps the password check to the error shown to the user, or null when it passed.</summary>
    public static Error? Reject(ReauthenticationResult result) => result switch
    {
        ReauthenticationResult.Verified => null,
        ReauthenticationResult.NoLocalPassword => PaymentErrors.PasswordRequired,
        _ => PaymentErrors.InvalidPassword
    };

    public static string Mask(string? account) =>
        string.IsNullOrEmpty(account) ? "(không có)" : account.Length <= 4 ? account : new string('*', account.Length - 4) + account[^4..];
}
