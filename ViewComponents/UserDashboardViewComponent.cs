using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;
using MyStoryTold.Services;

namespace MyStoryTold.ViewComponents;

public class UserDashboardViewComponent : Microsoft.AspNetCore.Mvc.ViewComponent
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFriendService _friendService;

    private const int CharsPerPage = 1800;

    public UserDashboardViewComponent(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IFriendService friendService)
    {
        _db = db;
        _userManager = userManager;
        _friendService = friendService;
    }

    public async Task<Microsoft.AspNetCore.Mvc.IViewComponentResult> InvokeAsync()
    {
        var userId = _userManager.GetUserId(HttpContext.User);
        if (string.IsNullOrEmpty(userId)) return View(new UserDashboardViewModel());

        // Posts (excluding drafts)
        var publishedPosts = await _db.LifeEventPosts
            .Where(p => p.OwnerUserId == userId && !p.IsDraft)
            .Select(p => new { p.EventYear, BodyLen = p.Body == null ? 0 : p.Body.Length })
            .ToListAsync();

        var totalPosts = publishedPosts.Count;
        var totalChars = publishedPosts.Sum(p => p.BodyLen);
        var estimatedPages = totalChars > 0 ? (int)Math.Ceiling((double)totalChars / CharsPerPage) : 0;
        var yearsWithPosts = publishedPosts.Select(p => p.EventYear).Distinct().Count();

        // Comments authored
        var totalComments = await _db.Comments.CountAsync(c => c.AuthorUserId == userId);

        // Edits made (versions where I was the editor — excluding the initial v1 of my own posts)
        var totalEdits = await _db.PostVersions.CountAsync(v => v.EditedByUserId == userId && v.VersionNumber > 1);

        // Friend groupings
        var friendList = await _friendService.GetFriendListAsync(userId);
        IEnumerable<FriendItemViewModel> ofTier(FriendTier t) => friendList.Friends.Where(f => f.Tier == t);

        var vm = new UserDashboardViewModel
        {
            TotalPosts = totalPosts,
            TotalComments = totalComments,
            TotalEdits = totalEdits,
            EstimatedPages = estimatedPages,
            YearsWithPosts = yearsWithPosts,
            Acquaintances = ofTier(FriendTier.Acquaintance).Select(ToCircle).ToList(),
            Friends = ofTier(FriendTier.Friend).Select(ToCircle).ToList(),
            Family = ofTier(FriendTier.Family).Select(ToCircle).ToList()
        };

        return View(vm);
    }

    private static FriendCircleItem ToCircle(FriendItemViewModel f)
    {
        var name = f.User.DisplayName ?? f.User.UserName ?? "?";
        return new FriendCircleItem
        {
            UserId = f.User.Id,
            Name = name,
            PhotoUrl = f.User.ProfilePhotoUrl,
            Initial = string.IsNullOrEmpty(name) ? "?" : name.Trim()[0].ToString().ToUpper()
        };
    }
}
