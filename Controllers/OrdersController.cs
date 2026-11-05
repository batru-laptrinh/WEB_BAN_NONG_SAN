using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class OrdersController(OrderService service, UserManager<ApplicationUser> users,
    ILogger<OrdersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        var ownerId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(ownerId)) return Challenge();
        try { return View(await service.GetCustomerListAsync(ownerId, page, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Unable to read customer order list.");
            return View(new OrderListViewModel { IsDataAvailable = false });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return NotFound();
        var ownerId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(ownerId)) return Challenge();
        try
        {
            var model = await service.GetCustomerDetailsAsync(id, ownerId, cancellationToken);
            return model is null ? NotFound() : View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Unable to read customer order details.");
            TempData["ErrorMessage"] = "Tạm thời chưa đọc được đơn hàng. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromRoute] Guid id, string? confirmation, bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return NotFound();
        var ownerId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(ownerId)) return Challenge();
        if (!confirmed || !ModelState.IsValid) return BadRequest("Vui lòng xác nhận yêu cầu hủy đơn.");
        try
        {
            var result = await service.CancelCustomerAsync(id, ownerId, confirmation, cancellationToken);
            if (result.Status == OrderOperationStatus.NotFound) return NotFound();
            if (result.Status == OrderOperationStatus.Forbidden) return Forbid();
            if (result.Status == OrderOperationStatus.InvalidConfirmation) return BadRequest(result.Message);
            TempData[result.Status == OrderOperationStatus.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Unable to process customer cancellation request.");
            TempData["ErrorMessage"] = "Chưa xác nhận được kết quả. Hãy tải lại đơn hàng trước khi thử lại.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
