using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Controllers;

[Authorize]
public class ChannelController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChannelController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // GET /Channel — list every channel (browse / index)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var channels = await _db.Channels
            .Include(c => c.Admin)
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Post counts per channel for the listing.
        var counts = await _db.LifeEventPosts
            .Where(p => p.ChannelId != null && !p.IsDraft)
            .GroupBy(p => p.ChannelId!.Value)
            .Select(g => new { ChannelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChannelId, x => x.Count);

        ViewBag.PostCounts = counts;
        return View(channels);
    }

    // GET /Channel/{slug} — single channel page (all posts in the channel)
    [HttpGet("/Channel/{slug}")]
    public async Task<IActionResult> Show(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return NotFound();
        var channel = await _db.Channels
            .Include(c => c.Admin)
            .FirstOrDefaultAsync(c => c.Slug == slug);
        if (channel == null) return NotFound();

        var posts = await _db.LifeEventPosts
            .Where(p => p.ChannelId == channel.Id && !p.IsDraft)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .Include(p => p.Channel)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var meId = _userManager.GetUserId(User);
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        var isChannelWriter = !string.IsNullOrEmpty(meId) && channel.AdminUserId == meId;

        ViewBag.IsChannelManager = isAdmin || isChannelWriter;
        ViewBag.Channel = channel;
        return View(posts);
    }
}
