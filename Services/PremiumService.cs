using Microsoft.AspNetCore.Identity;
using MyStoryTold.Helpers;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

/// <summary>
/// Single decision authority for "is this premium feature available to
/// this user right now?" Everywhere in the app that wants to gate (or
/// just tag) a feature goes through this service — so the day we flip
/// PremiumEnforcementActive on, every gate engages simultaneously
/// without touching any view or controller code.
///
/// Today: <see cref="IsAvailableAsync"/> returns true for everyone
/// because enforcement is OFF. The badge + gate UI use this same
/// service to render — they'll switch from "free during beta" tooltip
/// to actual locks the moment enforcement turns on.
/// </summary>
public interface IPremiumService
{
    /// <summary>Is this feature available to the user *right now*? When
    /// enforcement is off → true always. When on → checks the user's
    /// PremiumUntil (and tier for tier-restricted features).</summary>
    Task<bool> IsAvailableAsync(ApplicationUser? user, PremiumFeature feature);

    /// <summary>Is this feature tagged as premium at all? (i.e., listed
    /// in the catalog.) Always-true while the catalog is comprehensive,
    /// but kept as a method so we can add free-but-tagged features
    /// later without rewriting callers.</summary>
    bool IsPremium(PremiumFeature feature);

    /// <summary>The full catalog. Sorted by tier then by name. Empty
    /// list never returned — features always exist even if Built=false.</summary>
    IReadOnlyList<PremiumFeatureInfo> Catalog { get; }

    /// <summary>Lookup helper for the catalog. Returns null if the key
    /// somehow isn't in the catalog (shouldn't happen — kept defensive
    /// since views call this).</summary>
    PremiumFeatureInfo? Get(PremiumFeature feature);

    /// <summary>Whether enforcement is currently active. Views use this
    /// to decide between "free during beta" and "premium" tooltips.</summary>
    Task<bool> EnforcementActiveAsync();

    /// <summary>Per-feature availability mode. Default is
    /// <see cref="FeatureMode.All"/> until an admin sets it otherwise.</summary>
    Task<FeatureMode> GetModeAsync(PremiumFeature feature);

    /// <summary>Set the per-feature availability mode. Cleared back to
    /// default by passing <see cref="FeatureMode.All"/>.</summary>
    Task SetModeAsync(PremiumFeature feature, FeatureMode mode);
}

public class PremiumService : IPremiumService
{
    private readonly ISiteSettings _site;
    private readonly UserManager<ApplicationUser> _userManager;

    public PremiumService(ISiteSettings site, UserManager<ApplicationUser> userManager)
    {
        _site = site;
        _userManager = userManager;
        _byKey = Catalog.ToDictionary(f => f.Key);
    }

    // ── The catalog ─────────────────────────────────────────────────
    // Adding a new premium feature = add an enum value in
    // Models/PremiumFeature.cs and append a row here. Keep `Built`
    // accurate so the (eventual) /Premium pricing page can render
    // "available today" vs "coming soon" buckets without guessing.
    public IReadOnlyList<PremiumFeatureInfo> Catalog { get; } = new[]
    {
        new PremiumFeatureInfo {
            Key = PremiumFeature.PhotoPositioning,
            Name = "Photo positioning",
            Description = "Place each photo on the 4×8 layout grid with explicit position and size.",
            Tier = PremiumTier.Personal,
            Built = true
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.NewspaperBookLayout,
            Name = "Newspaper / Book layouts",
            Description = "Article-style rendering: two-column journal or chapter-style book layout for any post.",
            Tier = PremiumTier.Personal,
            Built = true
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.AudioRecording,
            Name = "Audio recording",
            Description = "Record voice notes directly inside a story.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.VideoRecording,
            Name = "Video recording",
            Description = "Record short video clips directly into a story.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.VoiceDictation,
            Name = "Voice → story dictation",
            Description = "Talk for a few minutes; get a clean transcript pre-filled into a Story.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.AiInterview,
            Name = "AI interview mode",
            Description = "AI asks thoughtful follow-up questions to draw out the story.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.SmartPrompts,
            Name = "Smart prompts",
            Description = "Personalised prompts driven by gaps and themes in your Working Index.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.AnniversaryFeed,
            Name = "Anniversary feed",
            Description = "On-this-day reminders and milestone alerts for your past stories.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.ViewAsBook,
            Name = "View as a book",
            Description = "Render your chronicle as a beautifully typeset book in the browser.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.WorldTravelMap,
            Name = "World travel map",
            Description = "Geographical map of every location you've written about.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.SelfHostExport,
            Name = "Self-host encrypted export",
            Description = "Annual full-data encrypted ZIP so your story is durable outside the platform.",
            Tier = PremiumTier.Personal,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.FamilyGroups,
            Name = "Family Groups",
            Description = "Shared spaces for family members to write together.",
            Tier = PremiumTier.Family,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.FamilyChat,
            Name = "Family chats",
            Description = "Group conversations scoped to a Family Group.",
            Tier = PremiumTier.Family,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.PeopleProfiles,
            Name = "People profiles",
            Description = "Create profiles for people you write about who aren't on the site — deceased family, relatives who'll never join. Once a profile exists you can tag them in stories and photos the same way you tag members. If they later join, you can link the profile to their real account. (Tagging existing members is free.)",
            Tier = PremiumTier.Family,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.FamilyTree,
            Name = "Family tree",
            Description = "Drag-and-drop tree builder: bubble avatars for each person (member or profile), lines for relations, simple and graphical. Click any node to jump to that person's stories.",
            Tier = PremiumTier.Family,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.CoAuthoring,
            Name = "Co-authoring drafts",
            Description = "Invite one other person to co-write a draft privately before publishing.",
            Tier = PremiumTier.Family,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.CrowdsourceMemory,
            Name = "Crowdsource a memory",
            Description = "Invite a family member to add their version + photos to a story you're writing.",
            Tier = PremiumTier.Family,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.HeirAccount,
            Name = "Heir / Successor account",
            Description = "Designate someone to inherit your chronicle. Posthumous read-only mode for Family tier.",
            Tier = PremiumTier.Legacy,
            Built = false
        },
        new PremiumFeatureInfo {
            Key = PremiumFeature.TimeLockedStories,
            Name = "Time-locked stories",
            Description = "Write a story today, deliver it on a future date to a chosen recipient.",
            Tier = PremiumTier.Legacy,
            Built = false
        }
    };

    private readonly Dictionary<PremiumFeature, PremiumFeatureInfo> _byKey;

    public bool IsPremium(PremiumFeature feature) => _byKey.ContainsKey(feature);

    public PremiumFeatureInfo? Get(PremiumFeature feature) =>
        _byKey.TryGetValue(feature, out var info) ? info : null;

    public Task<bool> EnforcementActiveAsync() =>
        _site.GetBoolAsync(ISiteSettings.PremiumEnforcementActive, false);

    // SiteSettings key for per-feature mode. Missing row = default = All.
    private static string ModeKey(PremiumFeature feature) =>
        "Premium.Mode." + feature.ToString();

    public async Task<FeatureMode> GetModeAsync(PremiumFeature feature)
    {
        var raw = await _site.GetStringAsync(ModeKey(feature), null);
        return raw switch
        {
            "premium" => FeatureMode.Premium,
            "off"     => FeatureMode.Off,
            _         => FeatureMode.All
        };
    }

    public Task SetModeAsync(PremiumFeature feature, FeatureMode mode)
    {
        var s = mode switch
        {
            FeatureMode.Premium => "premium",
            FeatureMode.Off     => "off",
            _                   => "all"
        };
        return _site.SetStringAsync(ModeKey(feature), s);
    }

    public async Task<bool> IsAvailableAsync(ApplicationUser? user, PremiumFeature feature)
    {
        // Unknown feature key — fail open. Defensive only; the enum
        // shouldn't drift from the catalog if both are maintained.
        if (!_byKey.TryGetValue(feature, out var info)) return true;

        // Admins always pass — they need to keep testing every feature,
        // even one that's been pulled out of circulation for users.
        var isAdmin = user != null
                      && (await _userManager.IsInRoleAsync(user, "Admin")
                          || await _userManager.IsInRoleAsync(user, "SuperAdmin"));

        var mode = await GetModeAsync(feature);
        return mode switch
        {
            FeatureMode.All     => true,
            FeatureMode.Off     => isAdmin,
            FeatureMode.Premium => isAdmin || (user != null && user.HasPremiumAtTier(info.Tier)),
            _                   => true
        };
    }
}
