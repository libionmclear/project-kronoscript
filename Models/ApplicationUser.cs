using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(50)]
    public string? DisplayName { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    /// <summary>What relatives actually call this user — "Nonno",
    /// "Bobby", "Aunt Lou". Shown on the Family Tree bubble and
    /// optionally beside the display name in member-facing surfaces.</summary>
    [MaxLength(80)]
    public string? Nickname { get; set; }

    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }

    /// <summary>True if the user wants to share month/day but not the year of birth
    /// (common privacy preference for older users). When true, displays show
    /// "March 4" instead of "March 4, 1972".</summary>
    public bool HideBirthYear { get; set; }

    /// <summary>Hide channel posts (admin-curated topical content) from this
    /// user's home feed. They can still find channels via direct links.</summary>
    public bool HideChannelsInFeed { get; set; }

    /// <summary>Hide posts from biographical / managed accounts (e.g. Caesar)
    /// from this user's home feed.</summary>
    public bool HideBiographicalInFeed { get; set; }

    /// <summary>Comma-separated channel Ids the user has muted individually
    /// (in addition to / independent of the all-channels switch above).</summary>
    [MaxLength(2000)]
    public string? MutedChannelIds { get; set; }

    /// <summary>Comma-separated biographical-account user Ids the user has
    /// muted individually.</summary>
    [MaxLength(2000)]
    public string? MutedBiographicalUserIds { get; set; }

    /// <summary>Highest site banner version this user has dismissed. When the
    /// admin updates the banner the version increments, so the same user sees
    /// the new copy even if they dismissed the previous one.</summary>
    public int LastDismissedBannerVersion { get; set; }

    /// <summary>Highest "what's new" version this user has seen / dismissed.
    /// When the admin posts a new release-notes entry the version increments
    /// and the modal fires once on next visit.</summary>
    public int LastSeenWhatsNewVersion { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; } // "Male" or "Female"

    [MaxLength(200)]
    public string? BirthPlace { get; set; }

    [MaxLength(200)]
    public string? CurrentLocation { get; set; }

    [MaxLength(500)]
    public string? ProfilePhotoUrl { get; set; }

    [MaxLength(500)]
    public string? ProfileCardBackgroundUrl { get; set; }

    public bool ShowOnlineStatus { get; set; } = true;

    /// <summary>Comma-separated ISO 3166-1 alpha-2 country codes (e.g. "IT,US")</summary>
    [MaxLength(200)]
    public string? Nationalities { get; set; }

    /// <summary>Language code used by the Translate button on posts (e.g. "en", "it", "fr"). Null falls back to English.</summary>
    [MaxLength(16)]
    public string? PreferredReadingLanguage { get; set; }

    /// <summary>UI display language preference. v1 stores the choice but UI strings
    /// are still rendered in English; full localization is incremental.</summary>
    [MaxLength(16)]
    public string? PreferredUiLanguage { get; set; }

    /// <summary>If set in the future, the user has active premium access
    /// until this timestamp. Null = no premium. Stored as a UTC expiry
    /// rather than a boolean so renewals, cancellations (let the paid
    /// period run out instead of revoking immediately), grace periods,
    /// and lifetime ("Legacy" tier, year 9999) all collapse to one
    /// field. Today: the column exists but PremiumEnforcementActive is
    /// off, so the value isn't consulted. Set by Stripe webhooks (or
    /// admin manual grant) when subscriptions go live.</summary>
    public DateTime? PremiumUntil { get; set; }

    /// <summary>Which pricing tier the user is on while PremiumUntil is
    /// active. "Personal" / "Family" / "Legacy". Null means no premium
    /// regardless of PremiumUntil. Used to gate tier-specific features
    /// (e.g., Family Tree wants tier >= Family).</summary>
    [MaxLength(32)]
    public string? PremiumTier { get; set; }

    /// <summary>Number of times the user has been locked out recently. Drives progressive
    /// lockout duration (1st = 5 min, 2nd+ = 30 min). Resets on successful login or password reset.</summary>
    public int RecentLockoutCount { get; set; }

    /// <summary>Last time the user did anything authenticated — login or any page load.
    /// Updated on Login and by LastSeenMiddleware on each request (throttled to once per 5 min).
    /// Used by the sidebar to decide who's been "active recently" beyond just posting.</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>Beta-agreement consent timestamp from the signup checkbox. Null = legacy account.</summary>
    public DateTime? AgreedToTermsAt { get; set; }

    /// <summary>"Completely private / off-the-record". When true, user is hidden from search and public discovery; existing connections retain their access.</summary>
    public bool IsCompletelyPrivate { get; set; }

    /// <summary>Voluntary suspension end. While set in the future, login is refused with a friendly message.</summary>
    public DateTime? SuspendedUntil { get; set; }

    /// <summary>Hash of the 6-digit code we emailed on Delete-account request. Cleared after use.</summary>
    [MaxLength(128)]
    public string? DeletionCodeHash { get; set; }

    /// <summary>When the deletion code stops being valid (typically request-time + 30 minutes).</summary>
    public DateTime? DeletionCodeExpiresAt { get; set; }

    /// <summary>Set when the user asks an admin to handle their account deletion (e.g. they
    /// can't access the email-code flow). Admin sees a queue at /Admin/DeletionRequests.</summary>
    public DateTime? AccountDeletionRequestedAt { get; set; }

    /// <summary>Distinct UTC calendar days this user has been authenticated on. Drives the
    /// "Logins / Devotion" badge ladder. Incremented by LastSeenMiddleware when the
    /// stored LastSeenAt falls on a different UTC day than "now".</summary>
    public int LoginDaysCount { get; set; }

    // Last-known badge tier per ladder. BadgeService bumps these when a user crosses
    // a threshold; the dashboard fires a celebratory popup when any of them increase
    // since the last view. Default 0 = no tier earned yet.
    public int LastBadgeLevelPosts { get; set; }
    public int LastBadgeLevelWords { get; set; }
    public int LastBadgeLevelConnections { get; set; }
    public int LastBadgeLevelComments { get; set; }
    public int LastBadgeLevelLogins { get; set; }

    /// <summary>True once we've shown the founding-badge celebration for this user
    /// (Genesis / Prologue / Chapter One). Gates the one-time level-up modal.</summary>
    public bool FoundingBadgeAcknowledged { get; set; }

    /// <summary>For biographical / managed accounts: the admin who manages this
    /// account. Null for ordinary self-registered users. When set, login is
    /// blocked — the admin posts on this user's behalf via the "Post as" picker.</summary>
    [MaxLength(450)]
    public string? ManagedByUserId { get; set; }

    /// <summary>The user (if any) whose share link or invite brought this
    /// account in. Set once at registration from either an Invitation
    /// token (?invite=) or a share-link ref (?ref=). Drives the "invited
    /// by X" attribution shown across the UI and referral analytics.</summary>
    [MaxLength(450)]
    public string? InvitedByUserId { get; set; }

    /// <summary>True for biographical / fictional / historical-figure accounts
    /// (e.g. a Caesar profile). Posts and the profile page render with a sepia
    /// accent so readers know it's not a real person's account.</summary>
    public bool IsBiographical { get; set; }

    /// <summary>Date span shown on a biographical profile, e.g. "100 BC – 44 BC"
    /// or "1813 – 1883". Free text — only displayed.</summary>
    [MaxLength(60)]
    public string? BiographicalEra { get; set; }

    /// <summary>One- or two-line summary shown on the biographical profile header.</summary>
    [MaxLength(500)]
    public string? BiographicalSummary { get; set; }

    // Per-field visibility (default Public)
    public ProfileFieldVisibility BirthDateVisibility       { get; set; } = ProfileFieldVisibility.Public;
    public ProfileFieldVisibility GenderVisibility          { get; set; } = ProfileFieldVisibility.Public;
    public ProfileFieldVisibility BirthPlaceVisibility      { get; set; } = ProfileFieldVisibility.Public;
    public ProfileFieldVisibility CurrentLocationVisibility { get; set; } = ProfileFieldVisibility.Public;
    public ProfileFieldVisibility NationalitiesVisibility   { get; set; } = ProfileFieldVisibility.Public;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<FriendConnection> SentFriendRequests { get; set; } = new List<FriendConnection>();
    public ICollection<FriendConnection> ReceivedFriendRequests { get; set; } = new List<FriendConnection>();
    public ICollection<RelativeConnection> RelativeConnectionsA { get; set; } = new List<RelativeConnection>();
    public ICollection<RelativeConnection> RelativeConnectionsB { get; set; } = new List<RelativeConnection>();
    public ICollection<LifeEventPost> Posts { get; set; } = new List<LifeEventPost>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<PostLike> Likes { get; set; } = new List<PostLike>();
}
