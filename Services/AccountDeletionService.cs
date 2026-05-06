using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Services;

public class AccountDeletionService : IAccountDeletionService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountDeletionService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        // Order matters: most user-FKs are Restrict-on-delete, so wipe rows that
        // reference the user before removing the user row itself.
        var ownedPostIds = await _db.LifeEventPosts
            .IgnoreQueryFilters()
            .Where(p => p.OwnerUserId == userId)
            .Select(p => p.Id)
            .ToListAsync();

        await _db.PostLikes.Where(l => l.UserId == userId).ExecuteDeleteAsync();
        await _db.CommentLikes.Where(l => l.UserId == userId).ExecuteDeleteAsync();
        await _db.Comments.Where(c => c.AuthorUserId == userId).ExecuteDeleteAsync();
        await _db.Messages.Where(m => m.SenderUserId == userId || m.RecipientUserId == userId).ExecuteDeleteAsync();
        await _db.FriendConnections
            .Where(f => f.RequesterUserId == userId || f.AddresseeUserId == userId)
            .ExecuteDeleteAsync();
        await _db.RelativeConnections
            .Where(r => r.UserAId == userId || r.UserBId == userId)
            .ExecuteDeleteAsync();

        // PostVersions where this user was the editor on someone else's post
        // (cascade handles versions of their own posts when posts are removed)
        await _db.PostVersions
            .Where(v => v.EditedByUserId == userId && !ownedPostIds.Contains(v.PostId))
            .ExecuteDeleteAsync();

        await _db.LifeEventPosts
            .IgnoreQueryFilters()
            .Where(p => p.OwnerUserId == userId)
            .ExecuteDeleteAsync();

        // Notifications cascade via FK on UserId; ActorUserId is set null.
        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded;
    }
}
