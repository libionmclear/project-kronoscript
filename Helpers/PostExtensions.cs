using MyStoryTold.Models;

namespace MyStoryTold.Helpers;

public static class PostExtensions
{
    /// <summary>True if <paramref name="userId"/> can edit, soft-delete, restore,
    /// or hard-delete this post — either because they own it directly, or
    /// because the post belongs to a biographical/managed account that they
    /// administer (admin posts as the bio account; admin retains management
    /// rights on the resulting posts).
    /// Requires <see cref="LifeEventPost.Owner"/> to be loaded for the
    /// managed-account branch; falls back to the direct-owner check if not.</summary>
    public static bool CanBeManagedBy(this LifeEventPost post, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (post.OwnerUserId == userId) return true;
        if (post.Owner != null && post.Owner.IsBiographical
            && !string.IsNullOrEmpty(post.Owner.ManagedByUserId)
            && post.Owner.ManagedByUserId == userId) return true;
        return false;
    }
}
