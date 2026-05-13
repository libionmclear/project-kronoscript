using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyStoryTold.Hubs;

/// <summary>
/// Tracks per-user presence with three states: <c>online</c> (at least
/// one active connection and not idle), <c>away</c> (connected but the
/// client reported idle), and <c>offline</c> (no connections, or the
/// user opted out via "Look offline"). Other clients listen on
/// <c>PresenceChanged(userId, status)</c> and replace the previous
/// snapshot whenever they reconnect via <c>PresenceSnapshot(map)</c>
/// where map = { userId: "online"|"away" }.
/// </summary>
[Authorize]
public class PresenceHub : Hub
{
    // userId → set of active connection IDs (one user can have multiple tabs)
    private static readonly ConcurrentDictionary<string, HashSet<string>> _connections = new();
    // userId → true if the user is currently idle (Away)
    private static readonly ConcurrentDictionary<string, byte> _away = new();
    // userId → true if the user is currently "Looking offline"
    private static readonly ConcurrentDictionary<string, byte> _hidden = new();
    private static readonly object _lock = new();

    public static IEnumerable<string> OnlineUserIds => _connections.Keys
        .Where(u => !_hidden.ContainsKey(u));
    public static bool IsOnline(string userId) =>
        _connections.ContainsKey(userId) && !_hidden.ContainsKey(userId);

    private static string StatusFor(string userId)
    {
        if (_hidden.ContainsKey(userId))       return "offline";
        if (!_connections.ContainsKey(userId)) return "offline";
        if (_away.ContainsKey(userId))         return "away";
        return "online";
    }

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

            if (firstConnection && !_hidden.ContainsKey(userId))
            {
                await Clients.All.SendAsync("PresenceChanged", userId, StatusFor(userId));
            }

            // Snapshot for the newcomer: full status map for currently
            // tracked users (online / away). Offline users aren't sent;
            // their absence implies offline.
            var snapshot = _connections.Keys
                .Where(u => !_hidden.ContainsKey(u))
                .ToDictionary(u => u, u => StatusFor(u));
            await Clients.Caller.SendAsync("PresenceSnapshot", snapshot);
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
                        _away.TryRemove(userId, out _);
                        wentOffline = true;
                    }
                }
            }

            if (wentOffline)
            {
                await Clients.All.SendAsync("PresenceChanged", userId, "offline");
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Client reports idle / active. When the user goes idle
    /// they're broadcast as Away; back to active flips them to Online.
    /// Hidden ("Look offline") users stay reported as offline either way.</summary>
    public async Task SetAway(bool away)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId)) return;
        if (away) _away.TryAdd(userId, 0);
        else      _away.TryRemove(userId, out _);
        if (!_hidden.ContainsKey(userId))
        {
            await Clients.All.SendAsync("PresenceChanged", userId, StatusFor(userId));
        }
    }

    /// <summary>Client toggles "Look offline". The hub flips broadcast
    /// state immediately so the rest of the chat sees the user disappear
    /// from the online list without waiting for the next reconnect.
    /// The DB-side ShowOnlineStatus flip happens in the controller — this
    /// hub method is the live-state half.</summary>
    public async Task SetVisibility(bool show)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId)) return;
        if (show)
        {
            _hidden.TryRemove(userId, out _);
            if (_connections.ContainsKey(userId))
                await Clients.All.SendAsync("PresenceChanged", userId, StatusFor(userId));
        }
        else
        {
            _hidden.TryAdd(userId, 0);
            await Clients.All.SendAsync("PresenceChanged", userId, "offline");
        }
    }
}
