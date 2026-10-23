using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Applications;

// A vote with what the voter is for this application's section (roles as they are now).
public record VoteFacts(string UserId, bool IsLeader, bool IsAdmin, VoteDecision Decision, bool IsFinal);

public static class VoteRules
{
    // Accepted / Rejected: a section leader and an admin – two distinct users – agree (an admin who leads the section counts once).
    // Disputed: such a leader/admin pair disagrees; an admin who hasn't voted then makes the final decision.
    // Any eligible vote against an agreeing pair forms a split pair with it, so a conflict always reads as Disputed.
    public static ApplicationStatus Decide(IReadOnlyCollection<VoteFacts> votes)
    {
        if (votes.FirstOrDefault(v => v.IsFinal) is { } final)
            return final.Decision == VoteDecision.Approve ? ApplicationStatus.Accepted : ApplicationStatus.Rejected;

        bool Pair(VoteDecision leader, VoteDecision admin) => votes.Any(l => l.IsLeader && l.Decision == leader
            && votes.Any(a => a.IsAdmin && a.UserId != l.UserId && a.Decision == admin));

        if (Pair(VoteDecision.Approve, VoteDecision.Reject) || Pair(VoteDecision.Reject, VoteDecision.Approve)) return ApplicationStatus.Disputed;
        if (Pair(VoteDecision.Approve, VoteDecision.Approve)) return ApplicationStatus.Accepted;
        if (Pair(VoteDecision.Reject, VoteDecision.Reject)) return ApplicationStatus.Rejected;
        return ApplicationStatus.Pending;
    }
}
