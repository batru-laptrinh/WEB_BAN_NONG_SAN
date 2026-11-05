using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AddressesController(ShippingAddressService addresses, UserManager<ApplicationUser> users,
    ILogger<AddressesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await addresses.GetListAsync(OwnerId, ct));

    [HttpGet]
    public IActionResult Create() => View(new AddressFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AddressFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        return FormResult(await addresses.CreateAsync(model, OwnerId, ct), model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken ct)
    {
        try
        {
            var model = await addresses.GetFormAsync(id, OwnerId, ct);
            return model is null ? NotFound() : View(model);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] int id, AddressFormViewModel model, CancellationToken ct)
    {
        // Even invalid posted forms must not render another owner's address URL.
        try
        {
            var current = await addresses.GetFormAsync(id, OwnerId, ct);
            if (current is null) return NotFound();
            model.IsCurrentDefault = current.IsCurrentDefault;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
        if (!ModelState.IsValid) return View(model);
        return FormResult(await addresses.EditAsync(id, model, OwnerId, ct), model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        try
        {
            var model = await addresses.GetDeleteAsync(id, OwnerId, ct);
            return model is null ? NotFound() : View(model);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] int id, string? confirmation, CancellationToken ct) =>
        RedirectResult(await addresses.DeleteAsync(id, confirmation, OwnerId, ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault([FromRoute] int id, string? confirmation, CancellationToken ct) =>
        RedirectResult(await addresses.SetDefaultAsync(id, confirmation, OwnerId, ct));

    private string OwnerId => users.GetUserId(User) ?? string.Empty;

    private IActionResult FormResult(AddressOperationResult result, AddressFormViewModel model)
    {
        if (result.Status == AddressOperationStatus.NotFound) return NotFound();
        if (result.Status == AddressOperationStatus.Success)
        {
            TempData["AddressSuccess"] = result.Message;
            return RedirectToAction(nameof(Index));
        }
        if (result.Status == AddressOperationStatus.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
        ModelState.AddModelError(string.Empty, result.Message);
        return View(model);
    }

    private IActionResult RedirectResult(AddressOperationResult result)
    {
        if (result.Status == AddressOperationStatus.NotFound) return NotFound();
        TempData[result.Status == AddressOperationStatus.Success ? "AddressSuccess" : "AddressError"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Unavailable()
    {
        logger.LogWarning("Shipping address page could not be loaded.");
        TempData["AddressError"] = "Chưa tải được địa chỉ. Vui lòng thử lại sau.";
        return RedirectToAction(nameof(Index));
    }
}
