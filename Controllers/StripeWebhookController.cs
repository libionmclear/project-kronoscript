using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;
using Stripe;

namespace MyStoryTold.Controllers;

/// <summary>
/// Receives Stripe webhook events at /api/stripe/webhook and updates the
/// user's premium state in response. Every request is signature-verified
/// against Stripe:WebhookSecret — unsigned or tampered requests get a 400
/// and never touch the database.
///
/// Six events are wired (matching the dashboard endpoint configuration):
///   checkout.session.completed     → first-time subscription, set tier + until
///   customer.subscription.created  → backstop for the same (rare-but-possible)
///   customer.subscription.updated  → renewal, tier change, status change
///   customer.subscription.deleted  → cancellation / unpaid lapse
///   invoice.payment_succeeded      → renewal payment cleared, extend until
///   invoice.payment_failed         → log + record analytics; user keeps access
///                                    until period end, then sub.deleted clears it
///
/// All handlers are idempotent — Stripe retries failed deliveries, so we
/// can receive the same event multiple times and must not double-apply.
/// </summary>
[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _db;
    private readonly IAnalyticsService _analytics;
    private readonly IFamilyPlanService _familyPlan;
    private readonly ILogger<StripeWebhookController> _log;

    public StripeWebhookController(
        IConfiguration config,
        ApplicationDbContext db,
        IAnalyticsService analytics,
        IFamilyPlanService familyPlan,
        ILogger<StripeWebhookController> log)
    {
        _config = config;
        _db = db;
        _analytics = analytics;
        _familyPlan = familyPlan;
        _log = log;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var sig  = Request.Headers["Stripe-Signature"].ToString();
        var secret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            _log.LogWarning("Stripe webhook hit but no Stripe:WebhookSecret is configured.");
            return BadRequest();
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, sig, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _log.LogWarning(ex, "Stripe webhook signature verification failed.");
            return BadRequest();
        }

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompleted((Stripe.Checkout.Session)stripeEvent.Data.Object);
                    break;
                case "customer.subscription.created":
                case "customer.subscription.updated":
                    await HandleSubscriptionUpsert((Subscription)stripeEvent.Data.Object);
                    break;
                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted((Subscription)stripeEvent.Data.Object);
                    break;
                case "invoice.payment_succeeded":
                    await HandleInvoicePaid((Invoice)stripeEvent.Data.Object);
                    break;
                case "invoice.payment_failed":
                    await HandleInvoiceFailed((Invoice)stripeEvent.Data.Object);
                    break;
                default:
                    // Unhandled but valid event — log and 200 so Stripe doesn't retry.
                    _log.LogInformation("Stripe webhook: unhandled event type {Type}", stripeEvent.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stripe webhook handler failed for event {Type} ({Id})",
                stripeEvent.Type, stripeEvent.Id);
            // Return 500 so Stripe retries — we'd rather replay than silently lose state.
            return StatusCode(500);
        }

        return Ok();
    }

    /// <summary>First-time subscription completion. Resolve our user from
    /// ClientReferenceId (or Metadata as a backstop), stamp tier + until,
    /// and stash the subscription id.</summary>
    private async Task HandleCheckoutCompleted(Stripe.Checkout.Session session)
    {
        var userId = session.ClientReferenceId
                  ?? (session.Metadata?.TryGetValue("kron_user_id", out var u) == true ? u : null);
        if (string.IsNullOrEmpty(userId))
        {
            _log.LogWarning("checkout.session.completed without kron user id — session {SessionId}", session.Id);
            return;
        }
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return;

        var tier = (session.Metadata?.TryGetValue("kron_tier", out var t) == true ? t : null) ?? "Personal";
        user.PremiumTier = tier;
        user.StripeCustomerId ??= session.CustomerId;
        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            user.StripeSubscriptionId = session.SubscriptionId;
        }
        // Conservative initial bump — the subscription.updated event will
        // pin the exact period end, but we want HasPremium() to flip on
        // immediately rather than waiting for that follow-up call.
        user.PremiumUntil = DateTime.UtcNow.AddMonths(1).AddDays(2);
        await _db.SaveChangesAsync();

        await _analytics.RecordAsync("subscription.started", user.Id, new
        {
            tier,
            stripeCustomer = session.CustomerId,
            stripeSubscription = session.SubscriptionId
        });
    }

    /// <summary>Subscription created or updated — sync tier + period end.</summary>
    private async Task HandleSubscriptionUpsert(Subscription sub)
    {
        var user = await ResolveUserFromCustomer(sub.CustomerId);
        if (user == null) return;

        user.StripeSubscriptionId = sub.Id;
        user.PremiumTier = TierFromSubscription(sub) ?? user.PremiumTier ?? "Personal";

        // PremiumUntil = the current period end + 2-day grace, so a missed
        // webhook beat doesn't cut access prematurely. cancel_at_period_end
        // means Stripe will keep them active until period end; either way
        // our PremiumUntil reflects 'when does access stop'.
        if (sub.Status == "active" || sub.Status == "trialing" || sub.Status == "past_due")
        {
            user.PremiumUntil = sub.CurrentPeriodEnd.AddDays(2);
        }
        else if (sub.Status == "canceled" || sub.Status == "incomplete_expired" || sub.Status == "unpaid")
        {
            user.PremiumUntil = DateTime.UtcNow.AddMinutes(-1);
            user.StripeSubscriptionId = null;
        }

        await _db.SaveChangesAsync();
        // Family plan: sync covered members so their PremiumUntil tracks
        // the owner's renewal cadence. If the owner's tier flipped away
        // from Family (downgrade), SyncCoverageAsync will detect it and
        // clear coverage automatically.
        await _familyPlan.SyncCoverageAsync(user.Id);
        await _analytics.RecordAsync("subscription.upserted", user.Id, new
        {
            sub.Id, sub.Status, periodEnd = sub.CurrentPeriodEnd, user.PremiumTier
        });
    }

    /// <summary>Subscription fully gone — clear tier + access.</summary>
    private async Task HandleSubscriptionDeleted(Subscription sub)
    {
        var user = await ResolveUserFromCustomer(sub.CustomerId);
        if (user == null) return;
        user.StripeSubscriptionId = null;
        user.PremiumUntil = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();
        // Owner lost access → every covered family member loses access too.
        await _familyPlan.ClearCoverageAsync(user.Id);
        await _analytics.RecordAsync("subscription.cancelled", user.Id, new
        {
            sub.Id, sub.CancellationDetails?.Reason
        });
    }

    /// <summary>Renewal payment landed — extend access.</summary>
    private async Task HandleInvoicePaid(Invoice invoice)
    {
        if (string.IsNullOrEmpty(invoice.SubscriptionId)) return;
        var user = await ResolveUserFromCustomer(invoice.CustomerId);
        if (user == null) return;

        // Use the invoice's period_end as the source of truth.
        var periodEnd = invoice.Lines?.Data?.FirstOrDefault()?.Period?.End;
        if (periodEnd.HasValue)
        {
            user.PremiumUntil = periodEnd.Value.AddDays(2);
        }
        await _db.SaveChangesAsync();
        // Family plan: members' PremiumUntil tracks the owner's.
        await _familyPlan.SyncCoverageAsync(user.Id);
        await _analytics.RecordAsync("subscription.renewed", user.Id, new
        {
            invoice.Id, amount = invoice.AmountPaid, periodEnd
        });
    }

    /// <summary>Renewal payment failed. We don't strip access immediately —
    /// Stripe will retry per its dunning settings, and only cancel after the
    /// retries are exhausted (subscription.deleted fires then). Just record
    /// the event so the team can chase if it recurs.</summary>
    private async Task HandleInvoiceFailed(Invoice invoice)
    {
        var user = await ResolveUserFromCustomer(invoice.CustomerId);
        if (user == null) return;
        await _analytics.RecordAsync("subscription.payment_failed", user.Id, new
        {
            invoice.Id, attempts = invoice.AttemptCount
        });
    }

    private async Task<ApplicationUser?> ResolveUserFromCustomer(string? customerId)
    {
        if (string.IsNullOrEmpty(customerId)) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
    }

    /// <summary>Read which tier this subscription represents from the price
    /// id on its first line item, by comparing to the configured price ids.</summary>
    private string? TierFromSubscription(Subscription sub)
    {
        var priceId = sub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrEmpty(priceId)) return null;
        if (priceId == _config["Stripe:PersonalPriceId"]) return "Personal";
        if (priceId == _config["Stripe:FamilyPriceId"])   return "Family";
        return null;
    }
}
