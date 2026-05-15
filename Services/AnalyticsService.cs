using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

/// <summary>
/// First-class business-event recorder. Writes append-only rows to
/// <c>UserEvents</c> for the events the team cares about (registration,
/// post-published, invites, share, login-day, referral-signup, etc.).
///
/// Never throw: analytics failures must never break user flows. All
/// errors are swallowed and logged at Warning so we notice without
/// blocking the request.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>Record a single business event. Fire-and-forget; never
    /// throws on the caller. Pass an anonymous object for data and it
    /// will be serialized to JSON.</summary>
    Task RecordAsync(string eventType, string? userId = null, object? data = null);

    /// <summary>Record a 'login.day' event at most once per UTC day for
    /// the given user. Idempotent so we can call it on every login.</summary>
    Task RecordLoginDayAsync(string userId);
}

public class AnalyticsService : IAnalyticsService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AnalyticsService> _log;

    public AnalyticsService(ApplicationDbContext db, ILogger<AnalyticsService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task RecordAsync(string eventType, string? userId = null, object? data = null)
    {
        try
        {
            var row = new UserEvent
            {
                EventType = eventType,
                UserId = userId,
                EventData = data == null ? null : JsonSerializer.Serialize(data),
                OccurredAt = DateTime.UtcNow
            };
            _db.UserEvents.Add(row);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AnalyticsService.RecordAsync failed for {EventType}", eventType);
        }
    }

    public async Task RecordLoginDayAsync(string userId)
    {
        try
        {
            var todayUtc = DateTime.UtcNow.Date;
            var alreadyLoggedToday = await _db.UserEvents
                .Where(e => e.UserId == userId
                            && e.EventType == "login.day"
                            && e.OccurredAt >= todayUtc)
                .AnyAsync();
            if (alreadyLoggedToday) return;
            await RecordAsync("login.day", userId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AnalyticsService.RecordLoginDayAsync failed");
        }
    }
}
