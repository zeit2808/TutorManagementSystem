using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.IntegrationTests;

public sealed class CenterMembershipRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CenterMemberRole.CenterAdmin, true)]
    [InlineData(CenterMemberRole.Teacher, true)]
    [InlineData(CenterMemberRole.Staff, true)]
    [InlineData(CenterMemberRole.CenterOwner, false)]
    public void Owners_hand_out_every_role_except_ownership(CenterMemberRole role, bool assignable) =>
        Assert.Equal(assignable, CenterMembership.IsAssignableByOwner(role));

    [Fact]
    public void Owner_membership_cannot_be_demoted_or_reactivated_by_center_rules()
    {
        var owner = CenterMembership.CreateOwner(Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => owner.ChangeRole(CenterMemberRole.Teacher));
        Assert.Throws<InvalidOperationException>(owner.Reactivate);
    }

    [Fact]
    public void Inactive_member_rejoins_with_the_new_role()
    {
        var member = CenterMembership.Create(Guid.NewGuid(), Guid.NewGuid(), CenterMemberRole.Staff, Now);
        member.Deactivate();
        member.Rejoin(CenterMemberRole.Teacher);
        Assert.True(member.IsActive);
        Assert.Equal(CenterMemberRole.Teacher, member.Role);
        Assert.Throws<InvalidOperationException>(() => member.Rejoin(CenterMemberRole.Staff));
    }

    [Fact]
    public void Member_invitations_cannot_grant_ownership() =>
        Assert.Throws<ArgumentException>(() => CenterInvitation.CreateMember(Guid.NewGuid(), "a@b.c", "A@B.C",
            CenterMemberRole.CenterOwner, "hash", Now.AddDays(1), Now, Guid.NewGuid()));

    [Theory]
    [InlineData("2026-10-22T01:00:00Z", "2026-10-22T17:00:00Z")] // 08:00 VN → midnight VN
    [InlineData("2026-10-22T16:59:00Z", "2026-10-22T17:00:00Z")] // 23:59 VN, same day
    [InlineData("2026-10-22T17:00:00Z", "2026-10-23T17:00:00Z")] // 00:00 VN next day
    public void Teacher_attendance_window_ends_at_midnight_vietnam_time(string startUtc, string expectedUtc) =>
        Assert.Equal(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture),
            CenterTime.EndOfLocalDay(DateTimeOffset.Parse(startUtc, System.Globalization.CultureInfo.InvariantCulture)));
}
