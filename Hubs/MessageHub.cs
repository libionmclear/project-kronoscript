using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyStoryTold.Hubs;

/// <summary>
/// Real-time direct-message push. Each authenticated client joins a group
/// named after their own user id; the InboxController.Send action calls
/// Clients.Group(recipientId).SendAsync("messageReceived", payload) to push
/// the new message to every open tab the recipient has. The conversation
/// view also gets a copy back for the sender's *other* tabs so a message
/// sent from phone shows up on the desktop without a refresh.
/// </summary>
[Authorize]
public class MessageHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
