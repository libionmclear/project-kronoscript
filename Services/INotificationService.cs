using MyStoryTold.Models;

namespace MyStoryTold.Services;

public interface INotificationService
{
    Task CreateAsync(string userId, NotificationType type, string text, string? linkUrl, string? actorUserId = null);
    Task<List<Notification>> GetRecentAsync(string userId, int limit = 20);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAllReadAsync(string userId);
}
