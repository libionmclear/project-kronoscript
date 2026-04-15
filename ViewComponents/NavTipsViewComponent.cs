using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.ViewComponents;

public class NavTipsViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;

    public NavTipsViewComponent(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        List<Tip> tips;
        try
        {
            tips = await _db.Tips
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ThenByDescending(t => t.CreatedAt)
                .Take(10)
                .ToListAsync();
        }
        catch
        {
            tips = new List<Tip>();
        }

        if (!tips.Any())
        {
            tips = new List<Tip>
            {
                new() { Type = TipType.New,  Text = "You can now tag friends in your life events!", SortOrder = 0 },
                new() { Type = TipType.Tip,  Text = "Use Export My Story to save your timeline as a document.", SortOrder = 1 },
                new() { Type = TipType.Tip,  Text = "Set post visibility to control who sees each memory.", SortOrder = 2 },
                new() { Type = TipType.Info, Text = "Invite relatives to co-author your family story.", SortOrder = 3 },
            };
        }

        return View(tips);
    }
}
