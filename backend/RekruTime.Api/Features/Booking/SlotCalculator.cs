namespace RekruTime.Api.Features.Booking;

public record Interval(DateTime Start, DateTime End);

// Interviews: their scheduled interviews as [Start, BlockedUntil) – to group a day's interviews around the same people (#110).
public record Interviewer(string Id, bool IsAdmin, IReadOnlySet<int> SectionIds, IReadOnlyList<Interval> Availability, int AssignedInterviews,
    IReadOnlyList<Interval>? Interviews = null);

public enum SlotColor { Green, Yellow }

// Suggested: right next to an interview of someone on the team – they're already there, so they travel once (#110).
public record Slot(DateTime Start, DateTime End, SlotColor Color, IReadOnlyList<string> Team, bool Suggested = false);

public record SlotQuery(
    IReadOnlySet<int> SectionIds,
    TimeSpan Duration,
    TimeSpan Buffer,
    DateTime From,                     // earliest start (caller applies recruitment period + minimum notice)
    DateTime To,                       // latest end of the room block (start + duration + buffer)
    IReadOnlyList<Interviewer> People,
    IReadOnlyList<Interval> Busy,      // scheduled interviews as [Start, BlockedUntil)
    TimeZoneInfo? Zone = null);        // club time zone – what "the same day" means (default UTC)

// Pure function: no DB, no clock. Used by the admin preview, the public slot list and the booking itself,
// so all three always agree. Rules: docs/PLAN.md §1.1.
public static class SlotCalculator
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    // How far from a neighbouring interview a slot still counts as "next to it" (#110).
    public static readonly TimeSpan SuggestGap = TimeSpan.FromMinutes(30);

    // How many more interviews fit: greedily take the earliest slot, skip everything overlapping it.
    // ponytail: per section – sections share people and the room, so totals across sections overestimate.
    public static int Capacity(IEnumerable<Slot> slots, TimeSpan block)
    {
        var (count, nextFree) = (0, DateTime.MinValue);
        foreach (var s in slots.OrderBy(s => s.Start))
            if (s.Start >= nextFree) (count, nextFree) = (count + 1, s.Start + block);
        return count;
    }

    public static IEnumerable<Slot> Calculate(SlotQuery q)
    {
        var block = q.Duration + q.Buffer; // room and interviewers are needed for the buffer too
        // Relevant = in one of the sections, or an admin (board member). Others can never be on the team.
        var people = q.People
            .Where(p => p.IsAdmin || p.SectionIds.Overlaps(q.SectionIds))
            .Select(p => p with { Availability = Merge(p.Availability) })
            .ToList();

        for (var t = RoundUp(q.From); t + block <= q.To; t += Step)
        {
            var (start, end) = (t, t + block);
            if (q.Busy.Any(b => b.Start < end && b.End > start)) continue;

            var available = people.Where(p => p.Availability.Any(a => a.Start <= start && a.End >= end)).ToList();
            var day = Day(start, q.Zone);
            if (FindTeam(available, q.SectionIds, p => (p.Interviews ?? []).Any(i => Day(i.Start, q.Zone) == day)) is { } team)
                yield return new Slot(start, start + q.Duration, SlotColor.Green, team.Select(p => p.Id).ToList(),
                    team.Any(p => (p.Interviews ?? []).Any(i => NextTo(i, start, end))));
            else if (available.Where(p => q.SectionIds.IsSubsetOf(p.SectionIds)).MinBy(p => (p.AssignedInterviews, p.Id)) is { } alone)
                yield return new Slot(start, start + q.Duration, SlotColor.Yellow, [alone.Id]); // last resort: one interviewer
        }
    }

    // Smallest team of ≥ 2 people with someone from every section; ties → most people already interviewing that day
    // (they're there anyway – #110), then least busy, then by id (deterministic).
    // ponytail: brute force over combinations – fine for tens of people; prune if a section ever has hundreds.
    static Interviewer[]? FindTeam(List<Interviewer> available, IReadOnlySet<int> sections, Func<Interviewer, bool> onSite)
    {
        for (var size = 2; size <= Math.Max(2, sections.Count) && size <= available.Count; size++)
        {
            var best = Combinations(available, size)
                .Where(team => sections.All(s => team.Any(p => p.SectionIds.Contains(s))))
                .MinBy(team => (-team.Count(onSite), team.Sum(p => p.AssignedInterviews), string.Join(",", team.Select(p => p.Id).Order())));
            if (best is not null) return best;
        }
        return null;
    }

    // The slot [start, end) starts at most SuggestGap after the interview's block ends, or ends at most SuggestGap before it starts.
    static bool NextTo(Interval interview, DateTime start, DateTime end) =>
        (start >= interview.End && start - interview.End <= SuggestGap) || (end <= interview.Start && interview.Start - end <= SuggestGap);

    static DateOnly Day(DateTime utc, TimeZoneInfo? zone) => DateOnly.FromDateTime(zone is null ? utc : TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

    static IEnumerable<Interviewer[]> Combinations(List<Interviewer> items, int size, int start = 0)
    {
        if (size == 0) { yield return []; yield break; }
        for (var i = start; i <= items.Count - size; i++)
            foreach (var rest in Combinations(items, size - 1, i + 1))
                yield return [items[i], .. rest];
    }

    // Overlapping or touching blocks → one block (10:00–10:30 + 10:30–11:00 = 10:00–11:00).
    static List<Interval> Merge(IEnumerable<Interval> intervals)
    {
        var merged = new List<Interval>();
        foreach (var i in intervals.OrderBy(i => i.Start))
            if (merged.Count > 0 && i.Start <= merged[^1].End)
                merged[^1] = merged[^1] with { End = i.End > merged[^1].End ? i.End : merged[^1].End };
            else
                merged.Add(i);
        return merged;
    }

    static DateTime RoundUp(DateTime t) => new((t.Ticks + Step.Ticks - 1) / Step.Ticks * Step.Ticks, DateTimeKind.Utc);
}
