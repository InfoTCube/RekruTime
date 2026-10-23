using RekruTime.Api.Data;
using RekruTime.Api.Features.Applications;
using static RekruTime.Api.Data.ApplicationStatus;
using static RekruTime.Api.Data.VoteDecision;

namespace RekruTime.Tests;

public class VoteRulesTests
{
    static VoteFacts Leader(string id, VoteDecision d) => new(id, IsLeader: true, IsAdmin: false, d, IsFinal: false);
    static VoteFacts Admin(string id, VoteDecision d, bool final = false) => new(id, IsLeader: false, IsAdmin: true, d, final);
    static VoteFacts AdminLeader(string id, VoteDecision d) => new(id, IsLeader: true, IsAdmin: true, d, IsFinal: false);
    static VoteFacts Nobody(string id, VoteDecision d) => new(id, IsLeader: false, IsAdmin: false, d, IsFinal: false);

    static ApplicationStatus Decide(params VoteFacts[] votes) => VoteRules.Decide(votes);

    [Fact] public void No_votes_is_pending() => Assert.Equal(Pending, Decide());
    [Fact] public void Leader_alone_is_pending() => Assert.Equal(Pending, Decide(Leader("l", Approve)));
    [Fact] public void Admin_alone_is_pending() => Assert.Equal(Pending, Decide(Admin("a", Approve)));
    [Fact] public void Two_leaders_without_admin_is_pending() => Assert.Equal(Pending, Decide(Leader("l1", Approve), Leader("l2", Approve)));
    [Fact] public void Two_admins_without_leader_is_pending() => Assert.Equal(Pending, Decide(Admin("a1", Approve), Admin("a2", Approve)));

    [Fact] public void Leader_and_admin_approve_is_accepted() => Assert.Equal(Accepted, Decide(Leader("l", Approve), Admin("a", Approve)));
    [Fact] public void Leader_and_admin_reject_is_rejected() => Assert.Equal(Rejected, Decide(Leader("l", Reject), Admin("a", Reject)));
    [Fact] public void Leader_and_admin_split_is_disputed() => Assert.Equal(Disputed, Decide(Leader("l", Approve), Admin("a", Reject)));
    [Fact] public void Admin_approves_leader_rejects_is_disputed() => Assert.Equal(Disputed, Decide(Leader("l", Reject), Admin("a", Approve)));

    // An admin who also leads the section is one person – a second admin is needed.
    [Fact] public void Admin_leader_alone_is_pending() => Assert.Equal(Pending, Decide(AdminLeader("x", Approve)));
    [Fact] public void Admin_leader_and_second_admin_is_accepted() => Assert.Equal(Accepted, Decide(AdminLeader("x", Approve), Admin("a", Approve)));
    [Fact] public void Admin_leader_and_plain_leader_is_accepted() => Assert.Equal(Accepted, Decide(AdminLeader("x", Approve), Leader("l", Approve)));

    [Fact] public void Extra_reject_against_agreeing_pair_is_disputed() =>
        Assert.Equal(Disputed, Decide(Leader("l", Approve), Admin("a1", Approve), Admin("a2", Reject)));

    [Fact] public void Second_leader_rejecting_after_agreement_is_disputed() =>
        Assert.Equal(Disputed, Decide(Leader("l1", Approve), Admin("a", Approve), Leader("l2", Reject)));

    [Fact] public void Leaders_split_without_admin_is_pending() => Assert.Equal(Pending, Decide(Leader("l1", Approve), Leader("l2", Reject)));

    // Votes of users who are no longer leaders/admins don't count.
    [Fact] public void Ineligible_votes_are_ignored() => Assert.Equal(Accepted, Decide(Leader("l", Approve), Admin("a", Approve), Nobody("n", Reject)));

    [Fact] public void Final_decision_wins() =>
        Assert.Equal(Rejected, Decide(Leader("l", Approve), Admin("a1", Reject), Admin("a2", Reject, final: true)));

    [Fact] public void Final_approve_accepts() =>
        Assert.Equal(Accepted, Decide(Leader("l", Approve), Admin("a1", Reject), Admin("a2", Approve, final: true)));
}
