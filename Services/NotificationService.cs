using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db) { _db = db; }

    public async Task CreateAsync(string userId, NotificationType type, string text, string? linkUrl, string? actorUserId = null)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (!string.IsNullOrEmpty(actorUserId) && actorUserId == userId) return; // never notify yourself
        if (text.Length > 500) text = text[..500];

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = type,
            Text = text,
            LinkUrl = linkUrl,
            ActorUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetRecentAsync(string userId, int limit = 20)
    {
        return await _db.Notifications
            .Where(n => n.UserId == userId)
            .Include(n => n.Actor)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null);
    }

    public async Task MarkAllReadAsync(string userId)
    {
        var now = DateTime.UtcNow;
        await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
    }
}
