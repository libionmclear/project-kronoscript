using MyStoryTold.Models;

namespace MyStoryTold.Helpers;

/// <summary>
/// Convenience checks on <see cref="ApplicationUser"/> for the premium
/// subscription state. The "is premium" question collapses to "has a
/// PremiumUntil set in the future." A boolean column would have
/// required updating every user the day a charge failed; the timestamp
/// lets billing webhooks set a paid-period end-date and the natural
/// passage of time handles cancellation.
/// </summary>
public static class PremiumUserExtensions
{
    public static bool HasPremium(this ApplicationUser? u) =>
        u != null && u.PremiumUntil != null && u.PremiumUntil > DateTime.UtcNow;

    /// <summary>True when the user has premium AND their stored tier is
    /// at least the one required. Tier ordering: Personal &lt; Family
    /// &lt; Legacy.</summary>
    public static bool HasPremiumAtTier(this ApplicationUser? u, PremiumTier required)
    {
        if (!u.HasPremium()) return false;
        if (string.IsNullOrEmpty(u!.PremiumTier)) return false;
        if (!Enum.TryParse<PremiumTier>(u.PremiumTier, ignoreCase: true, out var ut)) return false;
        return (int)ut >= (int)required;
    }
}
