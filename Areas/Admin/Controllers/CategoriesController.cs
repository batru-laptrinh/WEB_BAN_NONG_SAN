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
public sealed class CategoriesController(ApplicationDbContext db, ILogger<CategoriesController> logger) : Controller
{
    private const int PageSize = 12;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(100, search.Trim().Length)];
        try
        {
            var query = db.Categories.AsNoTracking();
            if (search != null) query = query.Where(x => x.Name.Contains(search));
            var count = await query.CountAsync(cancellationToken);
            var pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            page = Math.Clamp(page, 1, pages);
            return View(new CatalogAdminCategoryList
            {
                Search = search, Page = page, TotalPages = pages, TotalCount = count,
                Items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                    .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể tải danh mục sản phẩm.");
            return View(new CatalogAdminCategoryList { Search = search, IsDataAvailable = false });
        }
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new CatalogAdminCategoryForm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CatalogAdminCategoryForm model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return View("Form", model);
        var category = new Category
        {
            Name = model.Name.Trim(), Description = Clean(model.Description), IsActive = model.IsActive,
            Version = Guid.NewGuid().ToString("N")
        };
        db.Categories.Add(category);
        return await SaveAsync(model, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (category == null) return NotFound();
            return View("Form", new CatalogAdminCategoryForm
            {
                Id = category.Id, Name = category.Name, Description = category.Description,
                IsActive = category.IsActive, Version = category.Version
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể đọc danh mục sản phẩm.");
            TempData["ErrorMessage"] = "Chưa đọc được danh mục. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] int id, CatalogAdminCategoryForm model,
        CancellationToken cancellationToken = default)
    {
        model.Id = id;
        try
        {
            var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (category == null) return NotFound();
            if (string.IsNullOrWhiteSpace(model.Version) || model.Version != category.Version)
                return Changed();
            if (!ModelState.IsValid) return View("Form", model);
            category.Name = model.Name.Trim();
            category.Description = Clean(model.Description);
            category.IsActive = model.IsActive;
            category.Version = Guid.NewGuid().ToString("N");
            return await SaveAsync(model, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể cập nhật danh mục sản phẩm.");
            ModelState.AddModelError("", "Chưa cập nhật được danh mục. Vui lòng thử lại sau.");
            return View("Form", model);
        }
    }

    private async Task<IActionResult> SaveAsync(CatalogAdminCategoryForm model, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = model.Id == 0 ? "Đã thêm danh mục." : "Đã cập nhật danh mục.";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể lưu danh mục sản phẩm.");
            ModelState.AddModelError("", "Chưa lưu được danh mục. Vui lòng kiểm tra lại và thử sau.");
            return View("Form", model);
        }
    }

    private IActionResult Changed()
    {
        TempData["ErrorMessage"] = "Danh mục đã được thay đổi ở phiên khác. Vui lòng mở lại trước khi chỉnh sửa.";
        return RedirectToAction(nameof(Index));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
