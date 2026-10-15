namespace RekruTime.Api.Features.Booking;

public record Interval(DateTime Start, DateTime End);

public record Interviewer(string Id, bool IsAdmin, IReadOnlySet<int> SectionIds, IReadOnlyList<Interval> Availability, int AssignedInterviews);

public enum SlotColor { Green, Yellow }

public record Slot(DateTime Start, DateTime End, SlotColor Color, IReadOnlyList<string> Team);

public record SlotQuery(
    IReadOnlySet<int> SectionIds,
    TimeSpan Duration,
    TimeSpan Buffer,
    DateTime From,                     // earliest start (caller applies recruitment period + minimum notice)
    DateTime To,                       // latest end of the room block (start + duration + buffer)
    IReadOnlyList<Interviewer> People,
    IReadOnlyList<Interval> Busy);     // scheduled interviews as [Start, BlockedUntil)

// Pure function: no DB, no clock. Used by the admin preview, the public slot list and the booking itself,
// so all three always agree. Rules: docs/PLAN.md §1.1.
public static class SlotCalculator
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

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
            if (FindTeam(available, q.SectionIds) is { } team)
                yield return new Slot(start, start + q.Duration, SlotColor.Green, team);
            else if (available.Where(p => q.SectionIds.IsSubsetOf(p.SectionIds)).MinBy(p => (p.AssignedInterviews, p.Id)) is { } alone)
                yield return new Slot(start, start + q.Duration, SlotColor.Yellow, [alone.Id]); // last resort: one interviewer
        }
    }

    // Smallest team of ≥ 2 people with someone from every section; ties → least busy, then by id (deterministic).
    // ponytail: brute force over combinations – fine for tens of people; prune if a section ever has hundreds.
    static IReadOnlyList<string>? FindTeam(List<Interviewer> available, IReadOnlySet<int> sections)
    {
        for (var size = 2; size <= Math.Max(2, sections.Count) && size <= available.Count; size++)
        {
            var best = Combinations(available, size)
                .Where(team => sections.All(s => team.Any(p => p.SectionIds.Contains(s))))
                .MinBy(team => (team.Sum(p => p.AssignedInterviews), string.Join(",", team.Select(p => p.Id).Order())));
            if (best is not null) return best.Select(p => p.Id).ToList();
        }
        return null;
    }

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
