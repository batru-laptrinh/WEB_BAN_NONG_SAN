using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services.Admin;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class UsersController(
    AdminUserService service,
    UserManager<ApplicationUser> users,
    ILogger<UsersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var actorId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorId)) return Challenge();
        search = CleanSearch(search);
        try
        {
            return View(await service.GetListAsync(search, page, actorId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể đọc danh sách tài khoản khách hàng.");
            return View(new AdminUserListViewModel { Search = search, IsDataAvailable = false });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] string id, string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ValidId(id)) return NotFound();
        var actorId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorId)) return Challenge();
        SetNavigation(search, page);
        try
        {
            var model = await service.GetDetailsAsync(id, actorId, cancellationToken);
            return model == null ? NotFound() : View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể đọc thông tin tài khoản khách hàng.");
            TempData["ErrorMessage"] = "Tạm thời chưa đọc được thông tin tài khoản. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Index), new { search = CleanSearch(search), page = Math.Max(1, page) });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Confirm([FromRoute] string id, string operation,
        string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ValidId(id)) return NotFound();
        if (operation is not ("lock" or "unlock")) return BadRequest("Thao tác không hợp lệ.");
        var actorId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorId)) return Challenge();
        try
        {
            var details = await service.GetDetailsAsync(id, actorId, cancellationToken);
            if (details == null) return NotFound();
            if (details.IsAdmin || details.IsSelf) return Forbid();
            if ((operation == "lock" && !details.CanLock) || (operation == "unlock" && !details.CanUnlock))
            {
                TempData["ErrorMessage"] = "Trạng thái tài khoản đã thay đổi hoặc không cần thực hiện thao tác này.";
                return RedirectToDetails(id, search, page);
            }

            var model = await service.PrepareAsync(id, operation, actorId, cancellationToken);
            if (model == null)
            {
                TempData["ErrorMessage"] = "Thông tin tài khoản đã thay đổi. Vui lòng kiểm tra lại.";
                return RedirectToDetails(id, search, page);
            }
            model.Search = CleanSearch(search);
            model.Page = Math.Max(1, page);
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể chuẩn bị xác nhận trạng thái tài khoản.");
            TempData["ErrorMessage"] = "Tạm thời chưa chuẩn bị được xác nhận. Vui lòng thử lại sau.";
            return RedirectToDetails(id, search, page);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Lock([FromRoute] string id, string? confirmation,
        string? search, int page = 1, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "lock", confirmation, search, page, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Unlock([FromRoute] string id, string? confirmation,
        string? search, int page = 1, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "unlock", confirmation, search, page, cancellationToken);

    private async Task<IActionResult> ChangeAsync(string id, string operation, string? confirmation,
        string? search, int page, CancellationToken cancellationToken)
    {
        if (!ValidId(id)) return NotFound();
        var actorId = users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorId)) return Challenge();
        var result = await service.ChangeLockoutAsync(id, operation, confirmation ?? string.Empty,
            actorId, cancellationToken);
        switch (result.Status)
        {
            case AdminUserOperationStatus.NotFound: return NotFound();
            case AdminUserOperationStatus.Forbidden: return Forbid();
            case AdminUserOperationStatus.InvalidConfirmation: return BadRequest(result.Message);
            case AdminUserOperationStatus.Success: TempData["SuccessMessage"] = result.Message; break;
            default: TempData["ErrorMessage"] = result.Message; break;
        }
        return RedirectToDetails(id, search, page);
    }

    private RedirectToActionResult RedirectToDetails(string id, string? search, int page) =>
        RedirectToAction(nameof(Details), new { id, search = CleanSearch(search), page = Math.Max(1, page) });

    private void SetNavigation(string? search, int page)
    {
        ViewData["Search"] = CleanSearch(search);
        ViewData["Page"] = Math.Max(1, page);
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 450;
    private static string? CleanSearch(string? search) => string.IsNullOrWhiteSpace(search)
        ? null : search.Trim()[..Math.Min(100, search.Trim().Length)];
}
