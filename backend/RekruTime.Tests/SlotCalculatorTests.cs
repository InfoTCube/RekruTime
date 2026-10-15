using RekruTime.Api.Features.Booking;

namespace RekruTime.Tests;

public class SlotCalculatorTests
{
    const int Ai = 1, Web = 2, Bio = 3;
    static readonly DateTime Day = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    static DateTime At(int h, int m = 0) => Day.AddHours(h).AddMinutes(m);
    static Interval Free(int fromH, int toH, int fromM = 0, int toM = 0) => new(At(fromH, fromM), At(toH, toM));

    static Interviewer Person(string id, int[] sections, bool admin = false, int load = 0, params Interval[] free) =>
        new(id, admin, sections.ToHashSet(), free, load);

    static List<Slot> Slots(int[] sections, Interviewer[] people, Interval[]? busy = null, int duration = 30, int buffer = 0, DateTime? from = null, DateTime? to = null) =>
        SlotCalculator.Calculate(new SlotQuery(sections.ToHashSet(), TimeSpan.FromMinutes(duration), TimeSpan.FromMinutes(buffer),
            from ?? At(0), to ?? At(24), people, busy ?? [])).ToList();

    static List<string> Starts(IEnumerable<Slot> slots) => slots.Select(s => s.Start.ToString("HH:mm")).ToList();

    [Fact]
    public void Two_recruiters_from_section_give_green_slots_with_both()
    {
        var slots = Slots([Ai], [Person("a", [Ai], free: Free(10, 11)), Person("b", [Ai], free: Free(10, 11))]);

        Assert.Equal(["10:00", "10:15", "10:30"], Starts(slots));
        Assert.All(slots, s => Assert.Equal(SlotColor.Green, s.Color));
        Assert.All(slots, s => Assert.Equal(["a", "b"], s.Team.Order()));
        Assert.Equal(At(10, 30), slots[0].End); // End = start + duration (buffer not included)
    }

    [Fact]
    public void Admin_plus_section_recruiter_is_green()
    {
        var slots = Slots([Ai], [Person("admin", [Web], admin: true, free: Free(10, 11)), Person("a", [Ai], free: Free(10, 11))]);

        Assert.All(slots, s => Assert.Equal(SlotColor.Green, s.Color));
        Assert.Equal(3, slots.Count);
    }

    [Fact]
    public void Admins_without_anyone_from_the_section_give_nothing()
    {
        var slots = Slots([Ai], [Person("admin1", [Web], admin: true, free: Free(10, 11)), Person("admin2", [], admin: true, free: Free(10, 11))]);

        Assert.Empty(slots);
    }

    [Fact]
    public void Recruiter_from_another_section_does_not_count()
    {
        var slots = Slots([Ai], [Person("a", [Ai], free: Free(10, 11)), Person("w", [Web], free: Free(10, 11))]);

        Assert.All(slots, s => Assert.Equal(SlotColor.Yellow, s.Color)); // only "a" is relevant → one person
        Assert.All(slots, s => Assert.Equal(["a"], s.Team));
    }

    [Fact]
    public void Two_sections_need_someone_from_each()
    {
        var people = new[] { Person("a", [Ai], free: Free(10, 11)), Person("w", [Web], free: Free(10, 12)), Person("a2", [Ai], free: Free(11, 12)) };
        var slots = Slots([Ai, Web], people, duration: 60);

        Assert.Equal(["10:00", "11:00"], Starts(slots));
        Assert.Equal(["a", "w"], slots[0].Team.Order());
        Assert.Equal(["a2", "w"], slots[1].Team.Order());
    }

    [Fact]
    public void Two_people_from_the_same_section_do_not_cover_two_sections()
    {
        var slots = Slots([Ai, Web], [Person("a", [Ai], free: Free(10, 11)), Person("a2", [Ai], free: Free(10, 11))]);

        Assert.Empty(slots);
    }

    [Fact]
    public void Person_in_both_sections_alone_is_yellow()
    {
        var slots = Slots([Ai, Web], [Person("aw", [Ai, Web], free: Free(10, 11))]);

        Assert.Equal(3, slots.Count);
        Assert.All(slots, s => Assert.Equal((SlotColor.Yellow, "aw"), (s.Color, s.Team.Single())));
    }

    [Fact]
    public void Three_sections_can_need_a_three_person_team()
    {
        var people = new[] { Person("a", [Ai], free: Free(10, 11)), Person("w", [Web], free: Free(10, 11)), Person("b", [Bio], free: Free(10, 11)) };
        var slots = Slots([Ai, Web, Bio], people);

        Assert.All(slots, s => Assert.Equal(["a", "b", "w"], s.Team.Order()));
    }

    [Fact]
    public void Smallest_team_wins_when_one_person_covers_two_sections()
    {
        var people = new[] { Person("aw", [Ai, Web], free: Free(10, 11)), Person("b", [Bio], free: Free(10, 11)), Person("a", [Ai], free: Free(10, 11)) };
        var slots = Slots([Ai, Web, Bio], people);

        Assert.All(slots, s => Assert.Equal(["aw", "b"], s.Team.Order()));
    }

    [Fact]
    public void Green_slot_never_also_shows_as_yellow()
    {
        var slots = Slots([Ai], [Person("a", [Ai], free: Free(10, 11)), Person("b", [Ai], free: Free(10, 12))]);

        // 10:45 needs a until 11:15, but a leaves at 11:00 → only b → yellow
        Assert.Equal(["10:00", "10:15", "10:30", "10:45", "11:00", "11:15", "11:30"], Starts(slots));
        Assert.Equal([SlotColor.Green, SlotColor.Green, SlotColor.Green, SlotColor.Yellow, SlotColor.Yellow, SlotColor.Yellow, SlotColor.Yellow], slots.Select(s => s.Color));
    }

    [Fact]
    public void Scheduled_interview_blocks_the_room_for_every_section()
    {
        var people = new[] { Person("a", [Ai], free: Free(10, 12)), Person("b", [Ai], free: Free(10, 12)) };
        var slots = Slots([Ai], people, busy: [new Interval(At(10, 30), At(11))]); // e.g. another section's interview

        Assert.Equal(["10:00", "11:00", "11:15", "11:30"], Starts(slots));
    }

    [Fact]
    public void Buffer_keeps_the_room_and_interviewers_after_the_interview()
    {
        var people = new[] { Person("a", [Ai], free: Free(10, 12)), Person("b", [Ai], free: Free(10, 12)) };
        // Existing interview 10:00–10:30 + 10 min buffer → room busy until 10:40.
        var slots = Slots([Ai], people, busy: [new Interval(At(10), At(10, 40))], buffer: 10);

        Assert.Equal("10:45", Starts(slots)[0]);
        Assert.Equal("11:15", Starts(slots)[^1]); // 11:15 + 30 + 10 = 11:55 ≤ 12:00; 11:30 would end at 12:10
    }

    [Fact]
    public void Adjacent_availability_blocks_are_merged()
    {
        var slots = Slots([Ai], [Person("a", [Ai], free: [Free(10, 10, 0, 30), Free(10, 11, 30)]), Person("b", [Ai], free: Free(10, 11))], duration: 60);

        Assert.Equal(["10:00"], Starts(slots));
    }

    [Fact]
    public void Interview_longer_than_availability_gives_nothing()
    {
        var slots = Slots([Ai], [Person("a", [Ai], free: Free(10, 11)), Person("b", [Ai], free: Free(10, 11))], duration: 90);

        Assert.Empty(slots);
    }

    [Fact]
    public void Least_busy_team_is_picked()
    {
        var people = new[] { Person("a", [Ai], load: 5, free: Free(10, 11)), Person("b", [Ai], load: 0, free: Free(10, 11)), Person("c", [Ai], load: 1, free: Free(10, 11)) };
        var slots = Slots([Ai], people);

        Assert.All(slots, s => Assert.Equal(["b", "c"], s.Team.Order()));
    }

    [Fact]
    public void Start_is_rounded_up_to_the_step_and_range_end_respected()
    {
        var people = new[] { Person("a", [Ai], free: Free(8, 18)), Person("b", [Ai], free: Free(8, 18)) };
        var slots = Slots([Ai], people, from: At(10, 7), to: At(11, 10));

        Assert.Equal(["10:15", "10:30"], Starts(slots)); // 10:45 would end 11:15 > 11:10
    }

    [Fact]
    public void Slots_are_continuous_in_utc_across_the_dst_change()
    {
        // Europe/Warsaw leaves summer time on 2026-10-25 at 01:00 UTC; the calculator works purely in UTC.
        var start = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var free = new Interval(start, start.AddHours(2));
        var slots = SlotCalculator.Calculate(new SlotQuery(new HashSet<int> { Ai }, TimeSpan.FromMinutes(30), TimeSpan.Zero, start, start.AddHours(2),
            [Person("a", [Ai], free: free), Person("b", [Ai], free: free)], [])).ToList();

        Assert.Equal(7, slots.Count); // 00:00 … 01:30 every 15 min, no gap or duplicate at the switch
        Assert.All(slots.Zip(slots.Skip(1)), p => Assert.Equal(SlotCalculator.Step, p.Second.Start - p.First.Start));
    }
}
