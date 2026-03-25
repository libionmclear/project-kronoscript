using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public class RelativeService : IRelativeService
{
    private readonly ApplicationDbContext _db;

    public RelativeService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RelativeConnection> SendRequestAsync(string userAId, string userBId, RelationshipType type)
    {
        var existing = await _db.RelativeConnections
            .FirstOrDefaultAsync(r =>
                (r.UserAId == userAId && r.UserBId == userBId) ||
                (r.UserAId == userBId && r.UserBId == userAId));

        if (existing != null)
            throw new InvalidOperationException("A relative connection already exists between these users.");

        var connection = new RelativeConnection
        {
            UserAId = userAId,
            UserBId = userBId,
            RelationshipType = type,
            Status = RelativeConnectionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.RelativeConnections.Add(connection);
        await _db.SaveChangesAsync();
        return connection;
    }

    public async Task<bool> AcceptRequestAsync(int connectionId, string userId)
    {
        var conn = await _db.RelativeConnections.FindAsync(connectionId);
        if (conn == null || conn.UserBId != userId || conn.Status != RelativeConnectionStatus.Pending)
            return false;

        conn.Status = RelativeConnectionStatus.Accepted;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeclineRequestAsync(int connectionId, string userId)
    {
        var conn = await _db.RelativeConnections.FindAsync(connectionId);
        if (conn == null || conn.UserBId != userId || conn.Status != RelativeConnectionStatus.Pending)
            return false;

        _db.RelativeConnections.Remove(conn);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(int connectionId, string userId)
    {
        var conn = await _db.RelativeConnections.FindAsync(connectionId);
        if (conn == null) return false;
        if (conn.UserAId != userId && conn.UserBId != userId) return false;

        _db.RelativeConnections.Remove(conn);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<RelativeListViewModel> GetRelativeListAsync(string userId)
    {
        var connections = await _db.RelativeConnections
            .Include(r => r.UserA)
            .Include(r => r.UserB)
            .Where(r => r.UserAId == userId || r.UserBId == userId)
            .ToListAsync();

        var vm = new RelativeListViewModel();

        foreach (var conn in connections)
        {
            var isUserA = conn.UserAId == userId;
            var otherUser = isUserA ? conn.UserB : conn.UserA;

            var item = new RelativeItemViewModel
            {
                ConnectionId = conn.Id,
                User = otherUser,
                RelationshipType = conn.RelationshipType,
                Status = conn.Status,
                IsUserA = isUserA
            };

            if (conn.Status == RelativeConnectionStatus.Accepted)
                vm.Relatives.Add(item);
            else if (conn.Status == RelativeConnectionStatus.Pending && isUserA)
                vm.PendingSent.Add(item);
            else if (conn.Status == RelativeConnectionStatus.Pending && !isUserA)
                vm.PendingReceived.Add(item);
        }

        return vm;
    }
}
