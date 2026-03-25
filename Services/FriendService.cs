using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public class FriendService : IFriendService
{
    private readonly ApplicationDbContext _db;

    public FriendService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<FriendConnection> SendRequestAsync(string requesterId, string addresseeId)
    {
        // Check if connection already exists in either direction
        var existing = await _db.FriendConnections
            .FirstOrDefaultAsync(f =>
                (f.RequesterUserId == requesterId && f.AddresseeUserId == addresseeId) ||
                (f.RequesterUserId == addresseeId && f.AddresseeUserId == requesterId));

        if (existing != null)
            throw new InvalidOperationException("A connection already exists between these users.");

        var connection = new FriendConnection
        {
            RequesterUserId = requesterId,
            AddresseeUserId = addresseeId,
            Status = FriendConnectionStatus.Pending,
            Tier = FriendTier.Acquaintance,
            CreatedAt = DateTime.UtcNow
        };

        _db.FriendConnections.Add(connection);
        await _db.SaveChangesAsync();
        return connection;
    }

    public async Task<bool> AcceptRequestAsync(int connectionId, string userId)
    {
        var conn = await _db.FriendConnections.FindAsync(connectionId);
        if (conn == null || conn.AddresseeUserId != userId || conn.Status != FriendConnectionStatus.Pending)
            return false;

        conn.Status = FriendConnectionStatus.Accepted;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeclineRequestAsync(int connectionId, string userId)
    {
        var conn = await _db.FriendConnections.FindAsync(connectionId);
        if (conn == null || conn.AddresseeUserId != userId || conn.Status != FriendConnectionStatus.Pending)
            return false;

        _db.FriendConnections.Remove(conn);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> BlockAsync(int connectionId, string userId)
    {
        var conn = await _db.FriendConnections.FindAsync(connectionId);
        if (conn == null) return false;
        if (conn.RequesterUserId != userId && conn.AddresseeUserId != userId) return false;

        conn.Status = FriendConnectionStatus.Blocked;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(int connectionId, string userId)
    {
        var conn = await _db.FriendConnections.FindAsync(connectionId);
        if (conn == null) return false;
        if (conn.RequesterUserId != userId && conn.AddresseeUserId != userId) return false;

        _db.FriendConnections.Remove(conn);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetTierAsync(int connectionId, string userId, FriendTier tier)
    {
        var conn = await _db.FriendConnections.FindAsync(connectionId);
        if (conn == null || conn.Status != FriendConnectionStatus.Accepted) return false;
        // Only the addressee (profile owner who received) or requester can set tier
        if (conn.RequesterUserId != userId && conn.AddresseeUserId != userId) return false;

        conn.Tier = tier;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<FriendListViewModel> GetFriendListAsync(string userId)
    {
        var connections = await _db.FriendConnections
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.RequesterUserId == userId || f.AddresseeUserId == userId)
            .Where(f => f.Status != FriendConnectionStatus.Blocked)
            .ToListAsync();

        var vm = new FriendListViewModel();

        foreach (var conn in connections)
        {
            var isRequester = conn.RequesterUserId == userId;
            var otherUser = isRequester ? conn.Addressee : conn.Requester;

            var item = new FriendItemViewModel
            {
                ConnectionId = conn.Id,
                User = otherUser,
                Tier = conn.Tier,
                Status = conn.Status,
                IsRequester = isRequester
            };

            if (conn.Status == FriendConnectionStatus.Accepted)
                vm.Friends.Add(item);
            else if (conn.Status == FriendConnectionStatus.Pending && isRequester)
                vm.PendingSent.Add(item);
            else if (conn.Status == FriendConnectionStatus.Pending && !isRequester)
                vm.PendingReceived.Add(item);
        }

        return vm;
    }

    public async Task<List<UserSearchResult>> SearchUsersAsync(string query, string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<UserSearchResult>();

        var lowerQuery = query.ToLower();
        return await _db.Users
            .Where(u => u.Id != currentUserId)
            .Where(u => u.UserName!.ToLower().Contains(lowerQuery) ||
                        (u.DisplayName != null && u.DisplayName.ToLower().Contains(lowerQuery)) ||
                        u.Email!.ToLower().Contains(lowerQuery))
            .Take(20)
            .Select(u => new UserSearchResult
            {
                UserId = u.Id,
                UserName = u.UserName!,
                DisplayName = u.DisplayName,
                ProfilePhotoUrl = u.ProfilePhotoUrl
            })
            .ToListAsync();
    }
}
