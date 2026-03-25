using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

[Authorize]
public class ExportController : Controller
{
    private readonly IExportService _exportService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ExportController(IExportService exportService, UserManager<ApplicationUser> userManager)
    {
        _exportService = exportService;
        _userManager = userManager;
    }

    // GET: /Export
    [HttpGet]
    public IActionResult Index() => View();

    // POST: /Export/Docx
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Docx()
    {
        var userId = _userManager.GetUserId(User)!;
        var bytes = await _exportService.ExportPostsAsDocxAsync(userId);
        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "MyStoryTold_Export.docx");
    }

    // POST: /Export/Txt
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Txt()
    {
        var userId = _userManager.GetUserId(User)!;
        var bytes = await _exportService.ExportPostsAsTxtAsync(userId);
        return File(bytes, "text/plain", "MyStoryTold_Export.txt");
    }
}
