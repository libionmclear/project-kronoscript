using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MyStoryTold.Helpers;
using MyStoryTold.Models;
using MyStoryTold.Services;

namespace MyStoryTold.Controllers;

/// <summary>
/// Customer-facing Premium benefits / upgrade page + Stripe checkout
/// entry-point. The Subscribe button on /Premium routes through Checkout
/// here, which mints a Stripe-hosted checkout session and redirects the
/// user there. Stripe handles card collection; the webhook
/// (StripeWebhookController) flips premium on once the session completes.
/// </summary>
[Authorize]
public class PremiumController : Controller
{
    private readonly IPremiumService _premium;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStripeService _stripe;
    private readonly ILogger<PremiumController> _log;

    public PremiumController(
        IPremiumService premium,
        UserManager<ApplicationUser> userManager,
        IStripeService stripe,
        ILogger<PremiumController> log)
    {
        _premium = premium;
        _userManager = userManager;
        _stripe = stripe;
        _log = log;
    }

    // GET: /Premium
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        ViewBag.IsPremium      = user.HasPremium();
        ViewBag.CurrentTier    = user?.PremiumTier;
        ViewBag.PremiumUntil   = user?.PremiumUntil;
        ViewBag.Catalog        = _premium.Catalog;
        ViewBag.StripeReady    = _stripe.IsConfigured;
        return View();
    }

    /// <summary>POST /Premium/Checkout/Personal (or Family). Mints a Stripe
    /// Checkout Session for the chosen tier and 303-redirects the user to
    /// Stripe's hosted card form. Stripe sends them back to
    /// CheckoutSuccess / CheckoutCancel below when they finish or bail.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(string id)
    {
        if (string.IsNullOrEmpty(id)
            || !(id.Equals("Personal", StringComparison.OrdinalIgnoreCase)
                 || id.Equals("Family", StringComparison.OrdinalIgnoreCase)))
        {
            TempData["Error"] = "Choose a plan to continue.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!_stripe.IsConfigured)
        {
            TempData["Error"] = "Checkout isn't available right now. Please try again shortly.";
            return RedirectToAction(nameof(Index));
        }

        var successUrl = Url.Action(nameof(CheckoutSuccess), "Premium", null, Request.Scheme)
            + "?session_id={CHECKOUT_SESSION_ID}";
        var cancelUrl  = Url.Action(nameof(CheckoutCancel),  "Premium", null, Request.Scheme)!;

        try
        {
            var url = await _stripe.CreateCheckoutSessionAsync(user, id, successUrl!, cancelUrl);
            return Redirect(url);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stripe checkout session creation failed for {UserId} tier {Tier}", user.Id, id);
            TempData["Error"] = "We couldn't open checkout. Please try again — if it keeps happening, drop us a note.";
            return RedirectToAction(nameof(Index));
        }
    }

    // GET: /Premium/CheckoutSuccess?session_id=cs_test_...
    public IActionResult CheckoutSuccess(string? session_id)
    {
        TempData["Success"] = "Welcome to Premium! Your account is being activated — refresh in a few seconds if it isn't already.";
        ViewBag.SessionId = session_id;
        return View();
    }

    // GET: /Premium/CheckoutCancel
    public IActionResult CheckoutCancel()
    {
        TempData["Info"] = "Checkout cancelled. No charge was made — feel free to come back any time.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Premium/ManageSubscription — generates a Stripe
    /// Customer Portal session URL and redirects the user there. The
    /// portal lets them update payment method, cancel, see invoices,
    /// without us having to build any of those flows ourselves.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ManageSubscription()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var returnUrl = Url.Action(nameof(Index), "Premium", null, Request.Scheme)!;
        var url = await _stripe.CreatePortalSessionAsync(user, returnUrl);
        if (string.IsNullOrEmpty(url))
        {
            TempData["Error"] = "Can't open subscription management right now — try refreshing, or contact support.";
            return RedirectToAction(nameof(Index));
        }
        return Redirect(url);
    }
}
