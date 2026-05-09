namespace MyStoryTold.Helpers;

/// <summary>
/// Definition of the five achievement ladders. Each ladder has 10 tiers with
/// a title, threshold (count required), and badge image. Tier 0 = none earned.
/// Tiers 1..10 map to wwwroot/badges/transparent/{key}-NN.png.
/// (Solid versions remain in wwwroot/badges/ for any future use.)
/// </summary>
public static class BadgeLadders
{
    public record Tier(int Level, string Title, long Threshold);
    public record Ladder(string Key, string Name, string CountUnit, IReadOnlyList<Tier> Tiers);

    public static readonly Ladder Posts = new("posts", "Posts", "stories", new[]
    {
        new Tier(1,  "Drafter",          1),
        new Tier(2,  "Poster",           3),
        new Tier(3,  "Regular",          7),
        new Tier(4,  "Faithful",         15),
        new Tier(5,  "Loyalist",         30),
        new Tier(6,  "Standard-Bearer",  60),
        new Tier(7,  "Veteran",          100),
        new Tier(8,  "Champion",         175),
        new Tier(9,  "Elite",            300),
        new Tier(10, "Kronos Legend",    500)
    });

    public static readonly Ladder Words = new("words", "Words written", "words", new[]
    {
        new Tier(1,  "Scribe",            250),
        new Tier(2,  "Reporter",          1_000),
        new Tier(3,  "Writer",            3_000),
        new Tier(4,  "Bard",              8_000),
        new Tier(5,  "Wordsmith",         20_000),
        new Tier(6,  "Storyteller",       40_000),
        new Tier(7,  "Biographer",        90_000),
        new Tier(8,  "Chronicler",        150_000),
        new Tier(9,  "Laureate",          250_000),
        new Tier(10, "Annals of Kronos",  500_000)
    });

    public static readonly Ladder Connections = new("connections", "Connections", "people", new[]
    {
        new Tier(1,  "Wanderer",        1),
        new Tier(2,  "Acquaintance",    5),
        new Tier(3,  "Companion",       15),
        new Tier(4,  "Friend",          30),
        new Tier(5,  "Mingler",         60),
        new Tier(6,  "Networker",       125),
        new Tier(7,  "Socialite",       250),
        new Tier(8,  "Influencer",      500),
        new Tier(9,  "Opinion Leader",  1_000),
        new Tier(10, "People's Choice", 2_500)
    });

    public static readonly Ladder Comments = new("comments", "Comments", "comments", new[]
    {
        new Tier(1,  "Whisperer",       1),
        new Tier(2,  "Replier",         5),
        new Tier(3,  "Voice",           15),
        new Tier(4,  "Commentator",     35),
        new Tier(5,  "Insider",         75),
        new Tier(6,  "Insightful",      150),
        new Tier(7,  "Analyst",         300),
        new Tier(8,  "Counsellor",      500),
        new Tier(9,  "Sage",            1_000),
        new Tier(10, "Voice of Kronos", 2_000)
    });

    public static readonly Ladder Logins = new("logins", "Days active", "days", new[]
    {
        new Tier(1,  "Newcomer",          3),
        new Tier(2,  "Visitor",           7),
        new Tier(3,  "Returner",          14),
        new Tier(4,  "Member",            30),
        new Tier(5,  "Devotee",           60),
        new Tier(6,  "Steadfast",         100),
        new Tier(7,  "Faithful Reader",   180),
        new Tier(8,  "Keeper of Days",    365),
        new Tier(9,  "Eternal Presence",  600),
        new Tier(10, "Kronos Master",     1_000)
    });

    public static readonly IReadOnlyList<Ladder> All = new[]
    {
        Posts, Words, Connections, Comments, Logins
    };
}
