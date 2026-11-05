using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CartController(CheckoutService checkout, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var owner = users.GetUserId(User);
        if (owner is null) return Challenge();
        return View(await checkout.GetCartAsync(owner, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(CartAddInput input, CancellationToken ct)
    {
        var owner = users.GetUserId(User);
        if (owner is null) return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Sản phẩm hoặc số lượng không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }
        return Finish(await checkout.AddAsync(owner, input.ProductId, input.Quantity, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromRoute] int id, CartUpdateInput input, CancellationToken ct)
    {
        var owner = users.GetUserId(User);
        if (owner is null) return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Số lượng hoặc phiên cập nhật không hợp lệ. Vui lòng tải lại trang.";
            return RedirectToAction(nameof(Index));
        }
        return Finish(await checkout.UpdateAsync(owner, id, input.Quantity, input.MutationToken, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove([FromRoute] int id, string? mutationToken, CancellationToken ct)
    {
        var owner = users.GetUserId(User);
        if (owner is null) return Challenge();
        return Finish(await checkout.RemoveAsync(owner, id, mutationToken, ct));
    }

    private IActionResult Finish(CommerceResult result)
    {
        if (result.NotFound) return NotFound();
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
