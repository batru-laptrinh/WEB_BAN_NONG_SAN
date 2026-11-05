using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CheckoutService checkout, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? couponCode, CancellationToken ct)
    {
        var owner = users.GetUserId(User);
        if (owner is null) return Challenge();
        if (couponCode?.Length > 50) couponCode = null;
        return View(await checkout.GetCheckoutAsync(owner, couponCode, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder([Bind(Prefix = "Input")] CheckoutInput input, CancellationToken ct)
    {
        var owner = users.GetUserId(User);
        if (owner is null) return Challenge();
        if (ModelState.IsValid)
        {
            var result = await checkout.PlaceOrderAsync(owner, input, ct);
            if (result.Succeeded && result.OrderId.HasValue)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction("Details", "Orders", new { id = result.OrderId.Value });
            }
            ModelState.AddModelError("", result.Message);
        }
        var model = await checkout.GetCheckoutAsync(owner, input.CouponCode, ct);
        // Every failed attempt receives a fresh reviewed cart and token. Never keep a
        // stale browser token in ModelState when rendering that refreshed preview.
        ModelState.Remove("Input.CheckoutToken");
        model.Input.AddressId = input.AddressId;
        return View("Index", model);
    }
}
