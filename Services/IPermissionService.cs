using MyStoryTold.Models;

namespace MyStoryTold.Services;

public interface IPermissionService
{
    Task<FriendTier?> GetViewerTierAsync(string viewerUserId, string profileOwnerUserId);
    Task<bool> CanViewPostsAsync(string viewerUserId, string ownerUserId);
    Task<bool> CanCommentAsync(string viewerUserId, string ownerUserId);
    /// <summary>Comment permission for a specific post — biographical-profile
    /// posts and channel posts are open to everyone, otherwise the regular
    /// owner-tier rules apply.</summary>
    Task<bool> CanCommentOnPostAsync(string viewerUserId, LifeEventPost post);
    Task<bool> CanReorderAsync(string viewerUserId, string ownerUserId);
}
