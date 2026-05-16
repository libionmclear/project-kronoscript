using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using Stripe;
// Two 'Session' types live in Stripe.net (Checkout + BillingPortal) — we
// use fully-qualified names inline rather than 'using' both namespaces.

namespace MyStoryTold.Services;

/// <summary>
/// Thin wrapper over the Stripe SDK that owns three jobs:
///
///   1. Create a Checkout Session for a given user + tier, returning the
///      hosted-checkout URL the controller redirects to.
///   2. Create a Customer Portal session so a subscriber can manage / cancel
///      their subscription on Stripe's hosted UI.
///   3. Resolve a tier name ('Personal' / 'Family') to the configured Stripe
///      Price id, so the controller never sees raw price ids.
///
/// Webhook handling lives in <see cref="MyStoryTold.Controllers.StripeWebhookController"/>
/// — this service is the SEND side; the controller is the RECEIVE side.
/// </summary>
public interface IStripeService
{
    /// <summary>True when the keys/prices needed for checkout are populated.
    /// Used by the controller / view to hide the Subscribe button when we're
    /// running unconfigured (e.g., on a fresh dev box without Azure config).</summary>
    bool IsConfigured { get; }

    /// <summary>Create a Checkout Session and return its hosted URL. The
    /// session is bound to the user's existing StripeCustomerId, or to a
    /// freshly-created customer if this is the user's first checkout.</summary>
    Task<string> CreateCheckoutSessionAsync(ApplicationUser user, string tier, string successUrl, string cancelUrl);

    /// <summary>Create a Customer Portal session — the 'Manage subscription'
    /// link in the user's settings sends them here. Requires the user to have
    /// a StripeCustomerId already.</summary>
    Task<string?> CreatePortalSessionAsync(ApplicationUser user, string returnUrl);
}

public class StripeService : IStripeService
{
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<StripeService> _log;

    public StripeService(
        IConfiguration config,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<StripeService> log)
    {
        _config = config;
        _db = db;
        _userManager = userManager;
        _log = log;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_config["Stripe:SecretKey"]) &&
        !string.IsNullOrEmpty(_config["Stripe:PersonalPriceId"]) &&
        !string.IsNullOrEmpty(_config["Stripe:FamilyPriceId"]);

    private string? PriceIdForTier(string tier) => tier?.ToLowerInvariant() switch
    {
        "personal" => _config["Stripe:PersonalPriceId"],
        "family"   => _config["Stripe:FamilyPriceId"],
        _ => null
    };

    public async Task<string> CreateCheckoutSessionAsync(
        ApplicationUser user, string tier, string successUrl, string cancelUrl)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Stripe is not configured — set Stripe:SecretKey, Stripe:PersonalPriceId, Stripe:FamilyPriceId.");
        var priceId = PriceIdForTier(tier)
            ?? throw new ArgumentException($"Unknown tier '{tier}'. Use 'Personal' or 'Family'.", nameof(tier));

        // Ensure the user has a Stripe customer record. If we don't have one
        // on file, create it now so every subscription event ties back to a
        // single, stable customer id (avoids duplicate customer fragmentation
        // when the user subscribes, cancels, and re-subscribes later).
        if (string.IsNullOrEmpty(user.StripeCustomerId))
        {
            var customerSvc = new CustomerService();
            var customer = await customerSvc.CreateAsync(new CustomerCreateOptions
            {
                Email = user.Email,
                Name  = user.DisplayName ?? user.UserName,
                Metadata = new Dictionary<string, string>
                {
                    ["kron_user_id"] = user.Id
                }
            });
            user.StripeCustomerId = customer.Id;
            await _userManager.UpdateAsync(user);
        }

        var sessionSvc = new Stripe.Checkout.SessionService();
        var session = await sessionSvc.CreateAsync(new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            Customer = user.StripeCustomerId,
            LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            // Carry our user id + tier into the session so the webhook handler
            // (which sees only Stripe ids) can resolve them back to our records.
            ClientReferenceId = user.Id,
            Metadata = new Dictionary<string, string>
            {
                ["kron_user_id"] = user.Id,
                ["kron_tier"]    = tier
            },
            // Trial / billing config could live here once Marco picks a policy.
            AllowPromotionCodes = true
        });

        return session.Url;
    }

    public async Task<string?> CreatePortalSessionAsync(ApplicationUser user, string returnUrl)
    {
        if (!IsConfigured) return null;
        if (string.IsNullOrEmpty(user.StripeCustomerId)) return null;

        var svc = new Stripe.BillingPortal.SessionService();
        try
        {
            var session = await svc.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = user.StripeCustomerId,
                ReturnUrl = returnUrl
            });
            return session.Url;
        }
        catch (StripeException ex)
        {
            _log.LogWarning(ex, "Stripe portal session failed for user {UserId}", user.Id);
            return null;
        }
    }
}
