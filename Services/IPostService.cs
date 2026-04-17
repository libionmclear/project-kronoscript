using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public interface IPostService
{
    Task<LifeEventPost> CreatePostAsync(string userId, CreatePostViewModel model);
    Task<LifeEventPost?> GetPostAsync(int postId);
    Task<LifeEventPost?> EditPostAsync(int postId, string userId, EditPostViewModel model);
    Task<List<LifeEventPost>> GetTimelinePostsAsync(string ownerUserId, string sortBy, FriendTier? viewerTier, bool isOwner);
    Task<List<LifeEventPost>> GetFeedPostsAsync(string userId);
    Task<Comment> AddCommentAsync(string userId, AddCommentViewModel model);
    Task<bool> ToggleLikeAsync(int postId, string userId);
    Task<(ReactionType? reaction, int count)> ToggleReactionAsync(int postId, string userId, ReactionType reactionType);
    Task<bool> ReorderPostAsync(int postId, int newOrder, string userId);
    Task SaveMediaAsync(int postId, IFormFile file, MediaType mediaType);
}
