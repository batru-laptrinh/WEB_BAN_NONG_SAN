using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CouponsController(ApplicationDbContext db, ILogger<CouponsController> logger) : Controller
{
    private const int PageSize = 12;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToUpperInvariant();
        if (search?.Length > 50) search = search[..50];
        try
        {
            var query = db.Coupons.AsNoTracking();
            if (search != null) query = query.Where(x => x.Code.Contains(search));
            var count = await query.CountAsync(cancellationToken);
            var pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            page = Math.Clamp(page, 1, pages);
            return View(new CouponListViewModel
            {
                Search = search, Page = page, TotalPages = pages, TotalCount = count,
                Items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * PageSize)
                    .Take(PageSize).ToListAsync(cancellationToken)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không đọc được danh sách mã giảm giá.");
            return View(new CouponListViewModel { Search = search, IsDataAvailable = false });
        }
    }

    [HttpGet]
    public IActionResult Create()
    {
        var today = DateTime.UtcNow.AddHours(7).Date;
        return View("Form", new CouponFormViewModel { StartsAtLocal = today, EndsAtLocal = today.AddDays(7) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CouponFormViewModel model, CancellationToken cancellationToken = default)
    {
        model.Id = 0;
        try
        {
            await ValidateCodeAsync(model, cancellationToken);
            if (!ModelState.IsValid) return View("Form", model);
            db.Coupons.Add(new Coupon
            {
                Code = model.Code, Kind = model.Kind, Value = model.Value,
                MinimumSubtotal = model.MinimumSubtotal, MaxUses = model.MaxUses, IsActive = model.IsActive,
                StartsAtUtc = ToUtc(model.StartsAtLocal!.Value), EndsAtUtc = ToUtc(model.EndsAtLocal!.Value),
                UsedCount = 0, Version = Guid.NewGuid().ToString("N")
            });
            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = "Đã tạo mã giảm giá.";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không lưu được mã giảm giá mới.");
            ModelState.AddModelError("", "Chưa lưu được mã. Mã có thể đã tồn tại; kiểm tra lại trước khi thử lại.");
            return View("Form", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var coupon = await db.Coupons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (coupon == null) return NotFound();
            return View("Form", ToForm(coupon,
                coupon.UsedCount > 0 || await db.CouponUsages.AnyAsync(x => x.CouponId == id, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không đọc được mã giảm giá.");
            TempData["ErrorMessage"] = "Chưa đọc được mã giảm giá. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] int id, CouponFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        model.Id = id;
        try
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (coupon == null) return NotFound();
            if (string.IsNullOrWhiteSpace(model.Version) || model.Version != coupon.Version) return Changed();
            model.UsedCount = coupon.UsedCount;
            model.HasBeenUsed = coupon.UsedCount > 0 || await db.CouponUsages.AnyAsync(x => x.CouponId == id, cancellationToken);
            if (model.MaxUses < coupon.UsedCount)
                ModelState.AddModelError(nameof(model.MaxUses), "Tổng lượt không được nhỏ hơn số lượt đã sử dụng.");
            if (model.HasBeenUsed && (model.Code != coupon.Code || model.Kind != coupon.Kind || model.Value != coupon.Value
                || model.MinimumSubtotal != coupon.MinimumSubtotal
                || ToUtcOrNull(model.StartsAtLocal) != coupon.StartsAtUtc || ToUtcOrNull(model.EndsAtLocal) != coupon.EndsAtUtc))
                ModelState.AddModelError("", "Mã đã có lượt sử dụng: chỉ được thay đổi trạng thái bật/tắt và tổng lượt.");
            await ValidateCodeAsync(model, cancellationToken);
            if (!ModelState.IsValid) return View("Form", model);

            coupon.Code = model.Code;
            coupon.Kind = model.Kind;
            coupon.Value = model.Value;
            coupon.MinimumSubtotal = model.MinimumSubtotal;
            coupon.StartsAtUtc = ToUtc(model.StartsAtLocal!.Value);
            coupon.EndsAtUtc = ToUtc(model.EndsAtLocal!.Value);
            coupon.MaxUses = model.MaxUses;
            coupon.IsActive = model.IsActive;
            coupon.Version = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = "Đã cập nhật mã giảm giá.";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không cập nhật được mã giảm giá.");
            ModelState.AddModelError("", "Chưa cập nhật được mã. Vui lòng tải lại và kiểm tra trước khi thử lại.");
            return View("Form", model);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate([FromRoute] int id, string? version,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (coupon == null) return NotFound();
            if (string.IsNullOrWhiteSpace(version) || coupon.Version != version) return Changed();
            coupon.IsActive = false;
            coupon.Version = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = "Đã tắt mã giảm giá; lịch sử được giữ nguyên.";
        }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không tắt được mã giảm giá.");
            TempData["ErrorMessage"] = "Chưa tắt được mã giảm giá. Vui lòng kiểm tra và thử lại sau.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateCodeAsync(CouponFormViewModel model, CancellationToken ct)
    {
        if (await db.Coupons.AnyAsync(x => x.Code == model.Code && x.Id != model.Id, ct))
            ModelState.AddModelError(nameof(model.Code), "Mã giảm giá đã tồn tại.");
    }

    private IActionResult Changed()
    {
        TempData["ErrorMessage"] = "Mã giảm giá đã thay đổi ở phiên khác. Hãy mở lại trước khi chỉnh sửa.";
        return RedirectToAction(nameof(Index));
    }

    private static DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
    private static DateTime? ToUtcOrNull(DateTime? local) => local.HasValue && local.Value.Year is >= 2000 and <= 2100 ? ToUtc(local.Value) : null;
    private static CouponFormViewModel ToForm(Coupon coupon, bool used) => new()
    {
        Id = coupon.Id, Code = coupon.Code, Kind = coupon.Kind, Value = coupon.Value,
        MinimumSubtotal = coupon.MinimumSubtotal, MaxUses = coupon.MaxUses, IsActive = coupon.IsActive,
        StartsAtLocal = DateTime.SpecifyKind(coupon.StartsAtUtc.AddHours(7), DateTimeKind.Unspecified),
        EndsAtLocal = DateTime.SpecifyKind(coupon.EndsAtUtc.AddHours(7), DateTimeKind.Unspecified),
        Version = coupon.Version, UsedCount = coupon.UsedCount, HasBeenUsed = used
    };
}
