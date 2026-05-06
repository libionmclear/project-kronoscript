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

    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }

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
