using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Controllers;

[Authorize]
[Route("Media")]
public class MediaController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public MediaController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
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
    public async Task<IActionResult> AddComment(int mediaId, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("Empty");

        var media = await _db.PostMedia.FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null) return NotFound();

        var userId = _userManager.GetUserId(User)!;
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
}
