using MyStoryTold.Models;

namespace MyStoryTold.Services;

public interface IPermissionService
{
    Task<FriendTier?> GetViewerTierAsync(string viewerUserId, string profileOwnerUserId);
    Task<bool> CanViewPostsAsync(string viewerUserId, string ownerUserId);
    Task<bool> CanCommentAsync(string viewerUserId, string ownerUserId);
    Task<bool> CanReorderAsync(string viewerUserId, string ownerUserId);
}
