using System.ComponentModel.DataAnnotations;

namespace MyStoryTold.Models.ViewModels;

public class RegisterViewModel
{
    [Required, MaxLength(50)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = null!;

    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = null!;

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = null!;
}

public class LoginViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email")]
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

    [Display(Name = "Birth Year")]
    public int? BirthYear { get; set; }

    [Display(Name = "Birth Month")]
    public int? BirthMonth { get; set; }

    [Display(Name = "Birth Day")]
    public int? BirthDay { get; set; }

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

    [Display(Name = "Images")]
    public List<IFormFile>? Images { get; set; }

    [Display(Name = "Video")]
    public IFormFile? Video { get; set; }

    [Display(Name = "Tag Friends")]
    public List<string>? TaggedUserIds { get; set; }
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
}

public class AddCommentViewModel
{
    public int PostId { get; set; }

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
    public List<TaggedUserViewModel> TaggedUsers { get; set; } = new();
}

public class TaggedUserViewModel
{
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class TaggableFriendViewModel
{
    public string UserId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class PostDetailViewModel
{
    public LifeEventPost Post { get; set; } = null!;
    public string? DiffHtml { get; set; }
    public bool IsOwner { get; set; }
    public bool CanComment { get; set; }
    public int LikeCount { get; set; }
    public bool CurrentUserLiked { get; set; }
    public List<TaggedUserViewModel> TaggedUsers { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<TaggableFriendViewModel> TaggableFriends { get; set; } = new();
    public Dictionary<int, List<TaggedUserViewModel>> CommentMentions { get; set; } = new();
}

public class FriendListViewModel
{
    public List<FriendItemViewModel> Friends { get; set; } = new();
    public List<FriendItemViewModel> PendingReceived { get; set; } = new();
    public List<FriendItemViewModel> PendingSent { get; set; } = new();
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

public class FeedPostViewModel
{
    public LifeEventPost Post { get; set; } = null!;
    public int LikeCount { get; set; }
    public bool CurrentUserLiked { get; set; }
    public List<TaggedUserViewModel> TaggedUsers { get; set; } = new();
}

public class DashboardViewModel
{
    public List<FeedPostViewModel> RecentPosts { get; set; } = new();
    public int FriendsCount { get; set; }
    public int AcquaintancesCount { get; set; }
    public int FamilyCount { get; set; }
    public int TaggedCount { get; set; }
    public List<ActiveFriendViewModel> ActiveFriends { get; set; } = new();
}

public class ActiveFriendViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public DateTime? LastPostedAt { get; set; }
}

public class InviteViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Friend's Email")]
    public string Email { get; set; } = null!;

    [Required]
    [Display(Name = "Message")]
    public string Message { get; set; } = "Join me in writing our story together!";
}

public class UserSearchResult
{
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? ProfilePhotoUrl { get; set; }
}
