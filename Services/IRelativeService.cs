using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public interface IRelativeService
{
    Task<RelativeConnection> SendRequestAsync(string userAId, string userBId, RelationshipType type);
    Task<bool> AcceptRequestAsync(int connectionId, string userId);
    Task<bool> DeclineRequestAsync(int connectionId, string userId);
    Task<bool> RemoveAsync(int connectionId, string userId);
    Task<RelativeListViewModel> GetRelativeListAsync(string userId);
}
