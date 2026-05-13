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
    PeopleProfiles,
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
    SelfHostExport,
    /// <summary>Premium ability to create + admin a Channel
    /// (editorial topical bucket). Members can still read every
    /// channel without premium; creating one requires the tier.</summary>
    ChannelCreation
}

/// <summary>Pricing tier a feature belongs to. Eventually maps to the
/// Stripe Product/Price each subscriber owns.</summary>
public enum PremiumTier
{
    Personal,  // recurring, individual plan
    Family,    // recurring, family-group plan
    Legacy     // one-time purchase, perpetual access + heir features
}

/// <summary>Per-feature availability mode set in the admin catalog.
/// Admins always pass regardless of mode — even <see cref="Off"/> —
/// so they can keep testing a feature they've hidden from users.</summary>
public enum FeatureMode
{
    /// <summary>Available to every user.</summary>
    All = 0,
    /// <summary>Available only to active premium subscribers in the
    /// correct tier. Non-subscribers are blocked even when global
    /// enforcement is otherwise dormant.</summary>
    Premium = 1,
    /// <summary>Hidden from all non-admin users. Used to pull a feature
    /// out of circulation entirely (e.g. while debugging) without
    /// removing the code.</summary>
    Off = 2
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
