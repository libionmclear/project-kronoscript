using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyStoryTold.Hubs;

[Authorize]
public class PresenceHub : Hub
{
    // userId -> set of active connection IDs (one user can have multiple tabs)
    private static readonly ConcurrentDictionary<string, HashSet<string>> _connections = new();
    private static readonly object _lock = new();

    public static IEnumerable<string> OnlineUserIds => _connections.Keys;
    public static bool IsOnline(string userId) => _connections.ContainsKey(userId);

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            bool firstConnection;
            lock (_lock)
            {
                if (!_connections.TryGetValue(userId, out var set))
                {
                    set = new HashSet<string>();
                    _connections[userId] = set;
                }
                firstConnection = set.Count == 0;
                set.Add(Context.ConnectionId);
            }

            if (firstConnection)
            {
                await Clients.All.SendAsync("PresenceChanged", userId, true);
            }

            // Send current online list to the newcomer
            await Clients.Caller.SendAsync("PresenceSnapshot", _connections.Keys.ToArray());
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            bool wentOffline = false;
            lock (_lock)
            {
                if (_connections.TryGetValue(userId, out var set))
                {
                    set.Remove(Context.ConnectionId);
                    if (set.Count == 0)
                    {
                        _connections.TryRemove(userId, out _);
                        wentOffline = true;
                    }
                }
            }

            if (wentOffline)
            {
                await Clients.All.SendAsync("PresenceChanged", userId, false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
