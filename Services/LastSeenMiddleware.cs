using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

/// <summary>
/// Stamps ApplicationUser.LastSeenAt on each authenticated request so the
/// "active friends" sidebar can reflect login activity, not just posting.
/// Throttled per-user to once every <see cref="ThrottleWindow"/> via an
/// in-memory dictionary so we do at most ~1 UPDATE per user every 5 minutes.
/// Runs after the response so it never blocks the user's request.
/// </summary>
public class LastSeenMiddleware
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, DateTime> LastUpdated = new();

    private readonly RequestDelegate _next;

    public LastSeenMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext ctx, ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        await _next(ctx);

        if (ctx.User?.Identity?.IsAuthenticated != true) return;
        var userId = users.GetUserId(ctx.User);
        if (string.IsNullOrEmpty(userId)) return;

        var now = DateTime.UtcNow;
        if (LastUpdated.TryGetValue(userId, out var lastAt) && (now - lastAt) < ThrottleWindow)
            return;

        LastUpdated[userId] = now;

        try
        {
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, now));
        }
        catch
        {
            // Best-effort. A migration race or transient DB hiccup must not break navigation.
        }
    }
}
