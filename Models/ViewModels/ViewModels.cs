using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models.ViewModels;

/// <summary>
/// "Field must be true" — used for terms-of-service checkboxes. We
/// deliberately do NOT implement IClientModelValidator so no
/// data-val-* attribute is emitted, which avoids jQuery validate
/// mis-handling the value (its range validator treats string "true"
/// as NaN and rejects the field forever). Server-side validation
/// still fires from ModelState as usual.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is bool b && b;
}

public class RegisterViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = null!;

    [MaxLength(100)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    [Display(Name = "Last name")]
    public string? LastName { get; set; }

    [Required, MaxLength(50)]
    [RegularExpression(@"^[A-Za-z0-9._\-]+$",
        ErrorMessage = "Letters, numbers, dot, dash, underscore only — no spaces or other characters.")]
    [Display(Name = "Username")]
    public string UserName { get; set; } = null!;

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = null!;

    [MustBeTrue(ErrorMessage = "You must agree to the Privacy Policy and User Agreement to register.")]
    [Display(Name = "I have read and agree to the Privacy Policy and User Agreement")]
    public bool AgreedToTerms { get; set; }

    /// <summary>Honeypot — hidden via CSS, ignored by humans, filled by
    /// dumb bots. When non-empty we silently reject the registration
    /// (return the "we sent the email" page so the bot can't probe).</summary>
    public string? Website { get; set; }
}

public class LoginViewModel
{
    [Required]
    [Display(Name = "Email or Username")]
    public string Email { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = null!;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = null!;
}

public class ResetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Token { get; set; } = null!;

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string Password { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm New Password")]
    public string ConfirmPassword { get; set; } = null!;
}

public class ProfileEditViewModel
{
    [Required, MaxLength(50)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = null!;

    [MaxLength(100)]
    [Display(Name = "First Name")]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    [Display(Name = "Last Name")]
    public string? LastName { get; set; }

    [MaxLength(80)]
    [Display(Name = "Nickname")]
    public string? Nickname { get; set; }

    [Display(Name = "Birth Year")]
    public int? BirthYear { get; set; }

    [Display(Name = "Birth Month")]
    public int? BirthMonth { get; set; }

    [Display(Name = "Birth Day")]
    public int? BirthDay { get; set; }

    [Display(Name = "Hide year of birth")]
    public bool HideBirthYear { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; }

    [MaxLength(200)]
    [Display(Name = "Place of Birth")]
    public string? BirthPlace { get; set; }

    [MaxLength(200)]
    [Display(Name = "Residence")]
    public string? CurrentLocation { get; set; }

    [Display(Name = "Profile Photo")]
    public IFormFile? ProfilePhoto { get; set; }

    public string? ExistingPhotoUrl { get; set; }

    [Display(Name = "Card background image")]
    public IFormFile? ProfileCardBackground { get; set; }

    public string? ExistingCardBackgroundUrl { get; set; }

    [Display(Name = "Show online status")]
    public bool ShowOnlineStatus { get; set; } = true;

    [Display(Name = "Nationalities")]
    [MaxLength(200)]
    public string? Nationalities { get; set; }

    [Display(Name = "Preferred reading language")]
    [MaxLength(16)]
    public string? PreferredReadingLanguage { get; set; }

    [Display(Name = "App display language")]
    [MaxLength(16)]
    public string? PreferredUiLanguage { get; set; }

    [Display(Name = "Completely private (off-the-record)")]
    public bool IsCompletelyPrivate { get; set; }

    [Display(Name = "Hide channel posts from my feed")]
    public bool HideChannelsInFeed { get; set; }

    [Display(Name = "Hide biographical-profile posts from my feed")]
    public bool HideBiographicalInFeed { get; set; }

    /// <summary>Channel IDs the user wants to KEEP in their feed. Any channel
    /// not in this list (and present in the available list) is muted. Bound
    /// from a checkbox-per-channel list on the Settings page.</summary>
    public List<int> KeptChannelIds { get; set; } = new();

    /// <summary>Biographical-account user IDs the user wants to KEEP in
    /// their feed. Same opt-in semantics as <see cref="KeptChannelIds"/>.</summary>
    public List<string> KeptBiographicalUserIds { get; set; } = new();
}

/// <summary>Lightweight projection for the channel/bio checkbox lists on the
/// Settings page. Display-only; not bound back from the form.</summary>
public class SubscribableItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Icon { get; set; }
    public bool Kept { get; set; }
}

public class ChangePasswordViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current Password")]
    public string CurrentPassword { get; set; } = null!;

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    [Display(Name = "Confirm New Password")]
    public string ConfirmNewPassword { get; set; } = null!;
}

public class CreatePostViewModel
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [Required]
    [Display(Name = "Story")]
    public string Body { get; set; } = null!;

    [Required]
    [Display(Name = "Event Year")]
    public int EventYear { get; set; }

    [Display(Name = "Event Month")]
    public int? EventMonth { get; set; }

    [Display(Name = "Event Day")]
    public int? EventDay { get; set; }

    [Display(Name = "Date is estimated")]
    public bool EventDateIsEstimated { get; set; }

    [Display(Name = "Who can see this?")]
    public MyStoryTold.Models.PostVisibility Visibility { get; set; } = MyStoryTold.Models.PostVisibility.Friends;

    [MaxLength(200)]
    [Display(Name = "Location (optional)")]
    public string? Location { get; set; }

    [MaxLength(500)]
    [Display(Name = "Memory Music link")]
    public string? MusicUrl { get; set; }

    [Display(Name = "Images")]
    public List<IFormFile>? Images { get; set; }

    [Display(Name = "Video")]
    public IFormFile? Video { get; set; }

    [Display(Name = "Tag Friends")]
    public List<string>? TaggedUserIds { get; set; }

    /// <summary>People-profile IDs tagged via the same tag widget. Bound
    /// from hidden "TaggedProfileIds" inputs the picker emits whenever
    /// a profile is chosen. Stored on the post in a parallel column.</summary>
    [Display(Name = "Tag people profiles")]
    public List<int>? TaggedProfileIds { get; set; }

    // URLs of images pasted directly into the body textarea (uploaded via /Posts/UploadPastedImage)
    public List<string>? PastedImageUrls { get; set; }

    public bool IsDraft { get; set; }

    /// <summary>Channel this post is being published into (admin / channel-writer only).</summary>
    public int? ChannelId { get; set; }

    /// <summary>When an admin is publishing on behalf of a biographical/managed
    /// account they own, this carries that user's Id. The service silently
    /// drops it if the caller isn't an admin or doesn't own the target account.</summary>
    public string? PostAsUserId { get; set; }

    /// <summary>Visual layout chosen for this post. Defaults to Standard;
    /// the Create page seeds it from the channel's DefaultLayoutStyle when
    /// a channel is pre-selected, or Book when posting as a biographical.</summary>
    public MyStoryTold.Models.PostLayoutStyle LayoutStyle { get; set; } = MyStoryTold.Models.PostLayoutStyle.Standard;

    /// <summary>Set when the writer arrived via "Connect your memory to this
    /// story" on a channel article — the new post will carry a back-link to
    /// that source post, and the source's detail page surfaces the count.</summary>
    public int? MemoryOfPostId { get; set; }

    /// <summary>Display name of the source article — only used in the form
    /// banner so the writer sees what they're responding to.</summary>
    public string? MemoryOfPostTitle { get; set; }
}

public class EditPostViewModel
{
    public int PostId { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [Required]
    public string Body { get; set; } = null!;

    [Required]
    [Display(Name = "Event Year")]
    public int EventYear { get; set; }

    [Display(Name = "Event Month")]
    public int? EventMonth { get; set; }

    [Display(Name = "Event Day")]
    public int? EventDay { get; set; }

    [Display(Name = "Date is estimated")]
    public bool EventDateIsEstimated { get; set; }

    [Display(Name = "Who can see this?")]
    public MyStoryTold.Models.PostVisibility Visibility { get; set; } = MyStoryTold.Models.PostVisibility.Friends;

    [MaxLength(200)]
    [Display(Name = "Location (optional)")]
    public string? Location { get; set; }

    [Display(Name = "Images")]
    public List<IFormFile>? Images { get; set; }

    [Display(Name = "Video")]
    public IFormFile? Video { get; set; }

    public List<string>? TaggedUserIds { get; set; }
    public List<int>? TaggedProfileIds { get; set; }
    public List<TaggableFriendViewModel> TaggableFriends { get; set; } = new();
    public List<TaggableProfileViewModel> TaggableProfiles { get; set; } = new();

    // URLs of images pasted directly into the body textarea
    public List<string>? PastedImageUrls { get; set; }

    public bool IsDraft { get; set; }

    /// <summary>Visual layout: Standard / Newspaper / Book. Channel posts
    /// default to Newspaper and biographical to Book on first save; the
    /// owner/admin can override via the Edit form.</summary>
    public MyStoryTold.Models.PostLayoutStyle LayoutStyle { get; set; } = MyStoryTold.Models.PostLayoutStyle.Standard;

    /// <summary>Existing PostMedia IDs in the order the user dragged them on
    /// the Edit page. Service re-stamps SortOrder from this list.</summary>
    public List<int>? MediaOrder { get; set; }

    /// <summary>Focal point X percentages (0-100), aligned by index with
    /// <see cref="MediaOrder"/>. The writer click-sets these on each tile to
    /// pick what part of the photo shows in cover-cropped thumbnails.</summary>
    public List<int>? MediaFocusX { get; set; }

    /// <summary>Focal point Y percentages (0-100), aligned by index with
    /// <see cref="MediaOrder"/>.</summary>
    public List<int>? MediaFocusY { get; set; }

    /// <summary>Per-tile newspaper/book layout position picked on the Edit
    /// page. Aligned by index with <see cref="MediaOrder"/>. Values map to
    /// the same 9-cell grid as PostMedia.LayoutPosition.</summary>
    public List<string>? MediaLayoutPositions { get; set; }

    /// <summary>Cell-span widths (1 or 2) from the picker, aligned with
    /// MediaOrder. Drives how big the photo renders inside the article.</summary>
    public List<int>? MediaLayoutWidths { get; set; }

    /// <summary>Cell-span heights (1–8). Combined with width forms a
    /// rectangle on the 4×8 grid.</summary>
    public List<int>? MediaLayoutHeights { get; set; }

    /// <summary>Origin column on the 4×8 grid (0–3) per tile.</summary>
    public List<int>? MediaLayoutCols { get; set; }

    /// <summary>Origin row on the 4×8 grid (0–7) per tile.</summary>
    public List<int>? MediaLayoutRows { get; set; }
}

public class AddCommentViewModel
{
    public int PostId { get; set; }

    public int? ParentCommentId { get; set; }

    /// <summary>When set, the comment is scoped to a Family Group's surface
    /// rather than the post's personal-feed audience. Members of OTHER
    /// groups (or readers on the personal feed) won't see it.</summary>
    public int? FamilyGroupId { get; set; }

    [Required]
    public string Body { get; set; } = null!;

    [Display(Name = "Event Year")]
    public int? EventYear { get; set; }

    [Display(Name = "Event Month")]
    public int? EventMonth { get; set; }

    [Display(Name = "Event Day")]
    public int? EventDay { get; set; }

    [Display(Name = "Date is estimated")]
    public bool EventDateIsEstimated { get; set; }
}

public class TimelineViewModel
{
    public ApplicationUser ProfileUser { get; set; } = null!;
    public List<PostCardViewModel> Posts { get; set; } = new();
    public bool IsOwner { get; set; }
    public FriendTier? ViewerTier { get; set; }
    public bool CanComment { get; set; }
    public bool CanReorder { get; set; }
    public string SortBy { get; set; } = "created";
    public List<TaggableFriendViewModel> TaggableFriends { get; set; } = new();
}

public class PostCardViewModel
{
    public LifeEventPost Post { get; set; } = null!;
    public string? DiffHtml { get; set; }
    public int LikeCount { get; set; }
    public bool CurrentUserLiked { get; set; }
    public MyStoryTold.Models.ReactionType? CurrentUserReaction { get; set; }
    public List<TaggedUserViewModel> TaggedUsers { get; set; } = new();
    public List<TaggedProfileViewModel> TaggedProfiles { get; set; } = new();
}

public class TaggedUserViewModel
{
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

/// <summary>Profile-side equivalent for rendering "with X" chips when
/// the tag points to a PersonProfile instead of a real member.</summary>
public class TaggedProfileViewModel
{
    public int ProfileId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    /// <summary>If the profile has been claimed/linked to a real user,
    /// the chip can route there instead of the standalone profile page.</summary>
    public string? LinkedUserId { get; set; }
}

public class TaggableFriendViewModel
{
    public string UserId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

/// <summary>People-profile equivalent of TaggableFriendViewModel —
/// surfaced in the tag picker alongside real members so the writer
/// can tag deceased relatives / non-members they created profiles for.</summary>
public class TaggableProfileViewModel
{
    public int ProfileId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? Relation { get; set; }
    public string? AvatarUrl { get; set; }
}

public class PostDetailViewModel
{
    public LifeEventPost Post { get; set; } = null!;
    public string? DiffHtml { get; set; }
    public bool IsOwner { get; set; }
    /// <summary>True when the viewer can edit/delete this post — either as
    /// direct owner OR as the admin of the biographical account that owns it.</summary>
    public bool CanManage { get; set; }
    public bool CanComment { get; set; }
    public int LikeCount { get; set; }
    public bool CurrentUserLiked { get; set; }
    public MyStoryTold.Models.ReactionType? CurrentUserReaction { get; set; }
    public List<TaggedUserViewModel> TaggedUsers { get; set; } = new();
    public List<TaggedProfileViewModel> TaggedProfiles { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    /// <summary>Photo person-tags keyed by media id. Empty list when a photo has no tags.</summary>
    public Dictionary<int, List<MyStoryTold.Models.MediaPersonTag>> MediaPersonTags { get; set; } = new();
    public List<TaggableFriendViewModel> TaggableFriends { get; set; } = new();
    /// <summary>People-profiles the viewer can drop into a photo tag —
    /// their own profiles plus profiles created by family-tier
    /// connections. Used by the Detail-page "Tag a face" mode.</summary>
    public List<TaggableProfileViewModel> TaggableProfiles { get; set; } = new();
    /// <summary>True when the viewer is allowed to add face-tags to this
    /// post's photos. Owner, admin, and family-tier viewers qualify.</summary>
    public bool CanTagPhotos { get; set; }
    public Dictionary<int, List<TaggedUserViewModel>> CommentMentions { get; set; } = new();
    /// <summary>commentId → (likeCount, currentUserLiked)</summary>
    public Dictionary<int, (int Count, bool LikedByMe)> CommentLikes { get; set; } = new();

    /// <summary>How many other posts are linked back to this one as
    /// "memories of" — surfaces under the article as a count + list link.</summary>
    public int ConnectedMemoryCount { get; set; }

    /// <summary>If this post itself is a memory of another post, that source
    /// is materialised here so the detail page can show an "inspired by" chip.</summary>
    public LifeEventPost? MemoryOf { get; set; }
}

public class UserDashboardViewModel
{
    public int TotalPosts { get; set; }
    public int TotalComments { get; set; }
    public int TotalEdits { get; set; }
    public int EstimatedPages { get; set; }
    public int YearsWithPosts { get; set; }
    public List<FriendCircleItem> Acquaintances { get; set; } = new();
    public List<FriendCircleItem> Friends { get; set; } = new();
    public List<FriendCircleItem> Family { get; set; } = new();
}

public class FriendCircleItem
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Initial { get; set; } = "?";
    public string? PhotoUrl { get; set; }
}

public class OnboardingProgressViewModel
{
    public bool ShouldRender { get; set; }
    public bool HasProfile { get; set; }
    public bool HasFriend { get; set; }
    public bool HasPost { get; set; }
    public int Done { get; set; }
    public int Total { get; set; } = 3;
}

public class FriendListViewModel
{
    public List<FriendItemViewModel> Friends { get; set; } = new();
    public List<FriendItemViewModel> PendingReceived { get; set; } = new();
    public List<FriendItemViewModel> PendingSent { get; set; } = new();
    public List<RelativeItemViewModel> RelativeFamily { get; set; } = new();
}

public class FriendItemViewModel
{
    public int ConnectionId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public FriendTier Tier { get; set; }
    public FriendConnectionStatus Status { get; set; }
    public bool IsRequester { get; set; }
}

public class RelativeListViewModel
{
    public List<RelativeItemViewModel> Relatives { get; set; } = new();
    public List<RelativeItemViewModel> PendingReceived { get; set; } = new();
    public List<RelativeItemViewModel> PendingSent { get; set; } = new();
}

public class RelativeItemViewModel
{
    public int ConnectionId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public RelationshipType RelationshipType { get; set; }
    public RelativeConnectionStatus Status { get; set; }
    public bool IsUserA { get; set; }
}

public class FeedViewModel
{
    public List<FeedPostViewModel> Posts { get; set; } = new();
}

public class NotificationGroupViewModel
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string? Text { get; set; }
    public string Link { get; set; } = "#";
    public bool Unread { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ActorName { get; set; }
    public string? ActorPhoto { get; set; }
    public int GroupCount { get; set; }
}

public class FeedPostViewModel
{
    public LifeEventPost Post { get; set; } = null!;
    public int LikeCount { get; set; }
    public bool CurrentUserLiked { get; set; }
    public MyStoryTold.Models.ReactionType? CurrentUserReaction { get; set; }
    public List<TaggedUserViewModel> TaggedUsers { get; set; } = new();
    public List<TaggedProfileViewModel> TaggedProfiles { get; set; } = new();

    /// <summary>True when this post was injected into the feed by the
    /// evergreen-surfacing system (older channel/bio post being re-shown)
    /// rather than being part of the chronological cohort.</summary>
    public bool FromEvergreen { get; set; }

    /// <summary>"channel" or "bio" — what kind of evergreen pick this is.
    /// Used by the placement loop to avoid back-to-back inserts of the
    /// same kind, and (optionally) by the view to tag it.</summary>
    public string? EvergreenTag { get; set; }
}

public class DashboardViewModel
{
    public List<FeedPostViewModel> RecentPosts { get; set; } = new();
    public int FriendsCount { get; set; }
    public int AcquaintancesCount { get; set; }
    public int FamilyCount { get; set; }
    public int TaggedCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public List<ActiveFriendViewModel> ActiveFriends { get; set; } = new();
    public List<Tip> Tips { get; set; } = new();
    public bool IsNewUser { get; set; }
    public List<LifeEventPost> OnThisDay { get; set; } = new();
    public int NewAcquaintancesThisWeek { get; set; }
    public int NewFriendsThisWeek { get; set; }
    public int NewFamilyThisWeek { get; set; }
}

public class ActiveFriendViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public DateTime? LastPostedAt { get; set; }
    public bool IsOnline { get; set; }
}

public class InviteViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Friend's Email")]
    public string Email { get; set; } = null!;

    [Display(Name = "Subject")]
    [MaxLength(200)]
    public string? Subject { get; set; }

    [Required]
    [Display(Name = "Message")]
    public string Message { get; set; } =
        "I've started capturing my life story on Kronoscript — a place where memories get richer because the people who lived them can add their side. " +
        "I'd love for you to join and write some of these moments with me.";

    /// <summary>"send" = email it via Kronoscript; "link" = just generate a shareable link.</summary>
    public string Mode { get; set; } = "send";
}

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int ActiveUsersLast30Days { get; set; }
    public int ActiveUsersNow { get; set; }
    public int NewUsersThisWeek { get; set; }
    public int TotalPosts { get; set; }
    public int TotalComments { get; set; }
    public int TotalLikes { get; set; }
    public int ActiveBans { get; set; }
    public int PermanentBans { get; set; }
}

public class AdminUserViewModel
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PostCount { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsSuperAdmin { get; set; }
    public UserBan? ActiveBan { get; set; }
    /// <summary>1-based signup position across all users, oldest first
    /// (#1 = first ever signup). Used for the Genesis/Prologue/Chapter One
    /// founding badges and shown in the admin users list.</summary>
    public int Ordinal { get; set; }

    /// <summary>Premium expiry, surfaced in the admin Users list so the
    /// "Grant premium" button can read current state. Null = no premium.</summary>
    public DateTime? PremiumUntil { get; set; }
    public string? PremiumTier { get; set; }

    /// <summary>True once the user clicked the verification link in their
    /// signup email. Surfaced in the Users list so an admin can spot
    /// accounts that are stuck because the verification email never
    /// arrived (SendGrid failure, spam folder, etc.).</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>Non-null and in the future when Identity has locked the
    /// account after too many failed logins. The Users list shows a
    /// "locked until …" pill so the admin can unlock with one click.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
}

public class AdminBanViewModel
{
    [Required]
    public string UserId { get; set; } = null!;

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class UserSearchResult
{
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? ProfilePhotoUrl { get; set; }
}

public class TagWidgetViewModel
{
    public List<TaggableFriendViewModel> Friends { get; set; } = new();
    public List<TaggableFriendViewModel> Selected { get; set; } = new();
    public List<TaggableProfileViewModel> Profiles { get; set; } = new();
    public List<TaggableProfileViewModel> SelectedProfiles { get; set; } = new();
}

public class UserAvatarViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public int Size { get; set; } = 36;
}

public class AdminTipViewModel
{
    public int Id { get; set; }
    public MyStoryTold.Models.TipType Type { get; set; }
    public string Text { get; set; } = null!;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class InboxViewModel
{
    public List<ConversationSummaryViewModel> Conversations { get; set; } = new();
    public int TotalUnread { get; set; }
    public List<InboxContactViewModel> Family { get; set; } = new();
    public List<InboxContactViewModel> Friends { get; set; } = new();
    public List<InboxContactViewModel> Acquaintances { get; set; } = new();
}

public class InboxContactViewModel
{
    public ApplicationUser User { get; set; } = null!;
}

public class ConversationSummaryViewModel
{
    public ApplicationUser OtherUser { get; set; } = null!;
    public Message LastMessage { get; set; } = null!;
    public int UnreadCount { get; set; }
}

public class MessageThreadViewModel
{
    public ApplicationUser OtherUser { get; set; } = null!;
    public ApplicationUser? CurrentUser { get; set; }
    public List<Message> Messages { get; set; } = new();
    public string? ComposeToId { get; set; }
}
