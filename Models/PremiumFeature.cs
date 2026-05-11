namespace MyStoryTold.Models;

/// <summary>
/// Catalog of features that will eventually live behind the premium
/// subscription. Enum values are the stable keys used throughout the
/// codebase (views, controllers, services) — strings are too easy to
/// typo and silently drift when renamed.
///
/// Today, <see cref="MyStoryTold.Services.IPremiumService.IsAvailableAsync"/>
/// returns true for every feature for every user — enforcement is OFF
/// by default. When the admin flips the
/// <c>PremiumEnforcementActive</c> site setting on, the same checks
/// gate non-subscribers automatically, with zero code changes.
///
/// Adding a new premium feature = adding one enum value here + one
/// PremiumFeatureInfo entry in PremiumService.Catalog.
/// </summary>
public enum PremiumFeature
{
    PhotoPositioning,
    NewspaperBookLayout,
    AudioRecording,
    VideoRecording,
    FamilyGroups,
    FamilyChat,
    FamilyTree,
    ViewAsBook,
    WorldTravelMap,
    VoiceDictation,
    AiInterview,
    SmartPrompts,
    AnniversaryFeed,
    HeirAccount,
    TimeLockedStories,
    CoAuthoring,
    CrowdsourceMemory,
    SelfHostExport
}

/// <summary>Pricing tier a feature belongs to. Eventually maps to the
/// Stripe Product/Price each subscriber owns.</summary>
public enum PremiumTier
{
    Personal,  // recurring, individual plan
    Family,    // recurring, family-group plan
    Legacy     // one-time purchase, perpetual access + heir features
}

/// <summary>Descriptive metadata for a premium feature. Surfaces on
/// the admin catalog page, on the (eventual) public /Premium pricing
/// page, and in the badge/gate tooltips. Built=false means "tagged
/// as premium but not actually shipped yet" — a planned feature.</summary>
public class PremiumFeatureInfo
{
    public PremiumFeature Key { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public PremiumTier Tier { get; init; } = PremiumTier.Personal;
    public bool Built { get; init; }
}
