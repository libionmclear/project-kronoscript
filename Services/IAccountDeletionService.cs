namespace MyStoryTold.Services;

public interface IAccountDeletionService
{
    /// <summary>
    /// Wipe every row owned by or referencing this user (posts, comments, likes,
    /// connections, messages, notifications, etc.) in an order that respects FK
    /// restrict-on-delete constraints, then remove the user account itself.
    /// Returns true if the user was deleted, false if not found.
    /// </summary>
    Task<bool> DeleteUserAsync(string userId);
}
