using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public interface IFriendService
{
    Task<FriendConnection> SendRequestAsync(string requesterId, string addresseeId);
    Task<bool> AcceptRequestAsync(int connectionId, string userId, FriendTier tier = FriendTier.Acquaintance);
    Task<bool> DeclineRequestAsync(int connectionId, string userId);
    Task<bool> BlockAsync(int connectionId, string userId);
    Task<bool> RemoveAsync(int connectionId, string userId);
    Task<bool> SetTierAsync(int connectionId, string userId, FriendTier tier);
    Task<FriendListViewModel> GetFriendListAsync(string userId);
    Task<List<UserSearchResult>> SearchUsersAsync(string query, string currentUserId);
}
