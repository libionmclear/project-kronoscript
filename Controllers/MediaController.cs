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
        var isAdmin = User.IsInRole("Admin");
        var isOwner = media.Post.OwnerUserId == userId;
        // Family-tier viewers can tag faces in their family's photos —
        // it's the kind of thing a sibling does for their mom's album.
        var viewerTier = isOwner ? FriendTier.Family
                                 : await _permissions.GetViewerTierAsync(userId, media.Post.OwnerUserId);
        var isFamily = viewerTier == FriendTier.Family;
        if (!isOwner && !isAdmin && !isFamily)
        {
            return Forbid();
        }

        // Exactly one of the two targets must be set.
        var hasUser = !string.IsNullOrEmpty(targetUserId);
        var hasProfile = targetProfileId.HasValue && targetProfileId.Value > 0;
        if (hasUser == hasProfile) return BadRequest("Pick exactly one person.");

        // Validate the target. Members: must be the tagger or in their
        // network. Profiles: tagger's own, OR created by a family-tier
        // connection of the tagger (so family can use each other's NPCs).
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
            if (profile.CreatorUserId != userId)
            {
                // Family-tier with the profile creator + non-private profile.
                var creatorTier = await _permissions.GetViewerTierAsync(userId, profile.CreatorUserId);
                if (creatorTier != FriendTier.Family || profile.Visibility == PostVisibility.Private)
                {
                    return Forbid();
                }
            }
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

        string? label;
        string href;
        if (hasUser)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == targetUserId);
            label = u?.DisplayName ?? u?.UserName;
            href = Url.Action("Timeline", "Posts", new { id = targetUserId }) ?? "#";
        }
        else
        {
            var p = await _db.PersonProfiles.FirstOrDefaultAsync(x => x.Id == targetProfileId);
            label = p?.DisplayName;
            href = !string.IsNullOrEmpty(p?.LinkedUserId)
                ? Url.Action("Index", "Profile", new { id = p!.LinkedUserId }) ?? "#"
                : Url.Action("Details", "PersonProfiles", new { id = targetProfileId }) ?? "#";
        }
        return Json(new
        {
            id = tag.Id,
            x = tag.X,
            y = tag.Y,
            label,
            isProfile = hasProfile,
            href
        });
    }

    // Same as AddPersonTag but resolves the media row from its URL — the
    // Detail page's tag-mode emits image src instead of id because the
    // article-image figures aren't currently annotated with media ids.
    [HttpPost("AddPersonTagByUrl")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("user-write")]
    public async Task<IActionResult> AddPersonTagByUrl(string mediaUrl, string? targetUserId, int? targetProfileId, double x, double y)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl)) return BadRequest();
        var media = await _db.PostMedia.FirstOrDefaultAsync(m => m.Url == mediaUrl);
        if (media == null) return NotFound();
        return await AddPersonTag(media.Id, targetUserId, targetProfileId, x, y);
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
        // Owner, admin, family-tier viewer of the post, OR the person
        // who originally placed this tag could remove it. For now we
        // don't track "tag author", so: owner / admin / family.
        var isOwner = tag.Media.Post.OwnerUserId == userId;
        var isAdmin = User.IsInRole("Admin");
        var viewerTier = isOwner ? FriendTier.Family
                                 : await _permissions.GetViewerTierAsync(userId, tag.Media.Post.OwnerUserId);
        if (!isOwner && !isAdmin && viewerTier != FriendTier.Family)
        {
            return Forbid();
        }
        _db.MediaPersonTags.Remove(tag);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
