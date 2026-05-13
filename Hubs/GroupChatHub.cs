using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;

namespace MyStoryTold.Hubs;

/// <summary>
/// Realtime group chat. Clients subscribe to a per-group SignalR group
/// ("fg-{groupId}") when they open the chat panel; sending a message
/// persists it via <see cref="ApplicationDbContext"/> and fan-outs to
/// every connected client in that group. Membership is verified on
/// both Join and Send so a stranger can't shout into a group they're
/// not part of by knowing the id.
/// </summary>
[Authorize]
public class GroupChatHub : Hub
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public GroupChatHub(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    private static string GroupName(int familyGroupId) => $"fg-{familyGroupId}";

    public async Task JoinGroup(int familyGroupId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId)) return;
        var member = await _db.FamilyGroupMembers
            .AnyAsync(m => m.FamilyGroupId == familyGroupId && m.UserId == userId);
        if (!member) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(familyGroupId));
    }

    public async Task LeaveGroup(int familyGroupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(familyGroupId));
    }

    public async Task SendMessage(int familyGroupId, string body)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId)) return;
        if (string.IsNullOrWhiteSpace(body)) return;
        if (body.Length > 2000) body = body[..2000];

        var member = await _db.FamilyGroupMembers
            .AnyAsync(m => m.FamilyGroupId == familyGroupId && m.UserId == userId);
        if (!member) return;

        var msg = new GroupMessage
        {
            FamilyGroupId = familyGroupId,
            SenderUserId  = userId,
            Body          = body.Trim(),
            SentAt        = DateTime.UtcNow
        };
        _db.GroupMessages.Add(msg);
        await _db.SaveChangesAsync();

        var sender = await _userManager.FindByIdAsync(userId);
        await Clients.Group(GroupName(familyGroupId)).SendAsync("MessageReceived", new
        {
            id          = msg.Id,
            groupId     = familyGroupId,
            senderId    = userId,
            senderName  = sender?.DisplayName ?? sender?.UserName ?? "Someone",
            senderPhoto = sender?.ProfilePhotoUrl,
            body        = msg.Body,
            sentAt      = msg.SentAt
        });
    }
}
