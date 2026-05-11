using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
[Route("Media")]
public class MediaController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissions;

    public MediaController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IPermissionService permissions)
    {
        _db = db;
        _userManager = userManager;
        _permissions = permissions;
    }

    [HttpGet("Comments/{mediaId:int}")]
    public async Task<IActionResult> Comments(int mediaId)
    {
        var rows = await _db.MediaComments
            .Where(c => c.PostMediaId == mediaId)
            .Include(c => c.Author)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var data = rows.Select(c => new
        {
            id = c.Id,
            body = c.Body,
            createdAt = c.CreatedAt.ToString("MMM d, yyyy h:mm tt"),
            authorName = c.Author?.DisplayName ?? c.Author?.UserName ?? "Unknown",
            authorInitial = (c.Author?.FirstName?[0].ToString()
                            ?? c.Author?.UserName?[0].ToString() ?? "?").ToUpper(),
            authorPhoto = c.Author?.ProfilePhotoUrl
        });
        return Json(data);
    }

    [HttpPost("AddComment/{mediaId:int}")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> AddComment(int mediaId, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Empty");

        // Pull the parent post too so we can verify the commenter is
        // actually allowed to see this media. Without this check, a
        // user with a guessable mediaId could comment on something
        // they have no visibility into.
        var media = await _db.PostMedia
            .Include(m => m.Post)
            .FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null) return NotFound();

        var userId = _userManager.GetUserId(User)!;
        if (media.Post != null
            && media.Post.OwnerUserId != userId
            && media.Post.Visibility != PostVisibility.Public)
        {
            var canSee = await _permissions.CanViewPostsAsync(userId, media.Post.OwnerUserId);
            if (!canSee) return Forbid();
        }

        var comment = new MediaComment
        {
            PostMediaId = mediaId,
            AuthorUserId = userId,
            Body = body.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.MediaComments.Add(comment);
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(userId);
        return Json(new
        {
            id = comment.Id,
            body = comment.Body,
            createdAt = comment.CreatedAt.ToString("MMM d, yyyy h:mm tt"),
            authorName = user?.DisplayName ?? user?.UserName ?? "You",
            authorInitial = (user?.FirstName?[0].ToString()
                            ?? user?.UserName?[0].ToString() ?? "?").ToUpper(),
            authorPhoto = user?.ProfilePhotoUrl
        });
    }

    // ── Person tags on a photo (Facebook-style face tags) ──────────────
    //
    // Add: writer (post owner or admin) clicks on a photo in the Edit
    // page → picks a member or profile → label pinned at the click %.
    // Read: any viewer who can see the post sees the labels on the
    // Detail page; clicking a label opens that person.

    [HttpPost("AddPersonTag")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> AddPersonTag(int mediaId, string? targetUserId, int? targetProfileId, double x, double y)
    {
        var media = await _db.PostMedia
            .Include(m => m.Post)
            .FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null) return NotFound();
        if (media.Post == null) return NotFound();

        var userId = _userManager.GetUserId(User)!;
        // Only the post owner or an admin can tag. We don't allow third
        // parties to tag faces in someone else's photos.
        if (media.Post.OwnerUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        // Exactly one of the two targets must be set.
        var hasUser = !string.IsNullOrEmpty(targetUserId);
        var hasProfile = targetProfileId.HasValue && targetProfileId.Value > 0;
        if (hasUser == hasProfile) return BadRequest("Pick exactly one person.");

        // Validate the target the same way the body-tag picker does:
        // members must be in the writer's network (or self); profiles
        // must be created by the writer.
        if (hasUser)
        {
            if (targetUserId != userId)
            {
                var connected = await _db.FriendConnections.AnyAsync(f =>
                    f.Status == FriendConnectionStatus.Accepted
                    && ((f.RequesterUserId == userId && f.AddresseeUserId == targetUserId)
                     || (f.AddresseeUserId == userId && f.RequesterUserId == targetUserId)));
                if (!connected) return Forbid();
            }
        }
        else
        {
            var profile = await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == targetProfileId);
            if (profile == null) return NotFound();
            if (profile.CreatorUserId != userId) return Forbid();
        }

        var tag = new MediaPersonTag
        {
            PostMediaId = mediaId,
            TargetUserId = hasUser ? targetUserId : null,
            TargetProfileId = hasProfile ? targetProfileId : null,
            X = Math.Clamp(x, 0, 100),
            Y = Math.Clamp(y, 0, 100),
            CreatedAt = DateTime.UtcNow
        };
        _db.MediaPersonTags.Add(tag);
        await _db.SaveChangesAsync();

        return Json(new
        {
            id = tag.Id,
            x = tag.X,
            y = tag.Y,
            label = hasUser
                ? (await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId))?.DisplayName
                : (await _db.PersonProfiles.FirstOrDefaultAsync(p => p.Id == targetProfileId))?.DisplayName,
            isProfile = hasProfile
        });
    }

    [HttpPost("RemovePersonTag/{tagId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePersonTag(int tagId)
    {
        var tag = await _db.MediaPersonTags
            .Include(t => t.Media).ThenInclude(m => m!.Post)
            .FirstOrDefaultAsync(t => t.Id == tagId);
        if (tag == null) return NotFound();

        var userId = _userManager.GetUserId(User)!;
        if (tag.Media?.Post == null) return NotFound();
        if (tag.Media.Post.OwnerUserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        _db.MediaPersonTags.Remove(tag);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
