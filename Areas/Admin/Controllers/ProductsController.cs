using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Services;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProductsController(ApplicationDbContext db, ProductImageService images,
    ILogger<ProductsController> logger) : Controller
{
    private const int PageSize = 12;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(100, search.Trim().Length)];
        try
        {
            var query = db.Products.AsNoTracking().Include(x => x.Category).AsQueryable();
            if (search != null) query = query.Where(x => x.Name.Contains(search));
            var count = await query.CountAsync(cancellationToken);
            var pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            page = Math.Clamp(page, 1, pages);
            return View(new CatalogAdminProductList
            {
                Search = search, Page = page, TotalPages = pages, TotalCount = count,
                Items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                    .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể tải danh sách sản phẩm.");
            return View(new CatalogAdminProductList { Search = search, IsDataAvailable = false });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var model = new CatalogAdminProductForm();
        try { await LoadCategoriesAsync(model, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể tải danh mục cho biểu mẫu sản phẩm.");
            ModelState.AddModelError("", "Chưa tải được danh mục. Vui lòng thử lại sau.");
        }
        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductImageService.MaximumBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductImageService.MaximumBytes + 65536)]
    public async Task<IActionResult> Create(CatalogAdminProductForm model, CancellationToken cancellationToken = default)
    {
        return await PersistAsync(null, model, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (product == null) return NotFound();
            var model = new CatalogAdminProductForm
            {
                Id = product.Id, Name = product.Name, Description = product.Description, Unit = product.Unit,
                CategoryId = product.CategoryId, Price = product.Price, Stock = product.Stock,
                IsActive = product.IsActive, IsFeatured = product.IsFeatured,
                Version = product.Version, CurrentImagePath = product.ImagePath
            };
            await LoadCategoriesAsync(model, cancellationToken);
            return View("Form", model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể đọc thông tin sản phẩm.");
            TempData["ErrorMessage"] = "Chưa đọc được sản phẩm. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductImageService.MaximumBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductImageService.MaximumBytes + 65536)]
    public async Task<IActionResult> Edit([FromRoute] int id, CatalogAdminProductForm model,
        CancellationToken cancellationToken = default)
    {
        return await PersistAsync(id, model, cancellationToken);
    }

    private async Task<IActionResult> PersistAsync(int? id, CatalogAdminProductForm model,
        CancellationToken cancellationToken)
    {
        string? newImage = null;
        string? oldImage = null;
        model.Id = id ?? 0;
        try
        {
            Product? product = null;
            if (id.HasValue)
            {
                product = await db.Products.SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
                if (product == null) return NotFound();
                model.CurrentImagePath = product.ImagePath;
                if (string.IsNullOrWhiteSpace(model.Version) || model.Version != product.Version)
                    return Changed();
            }
            if (!await db.Categories.AnyAsync(x => x.Id == model.CategoryId && x.IsActive, cancellationToken))
                ModelState.AddModelError(nameof(model.CategoryId), "Danh mục không tồn tại hoặc đã ngừng sử dụng.");
            if (decimal.Round(model.Price, 2) != model.Price)
                ModelState.AddModelError(nameof(model.Price), "Giá chỉ được có tối đa hai chữ số thập phân.");
            if (!ModelState.IsValid)
            {
                await LoadCategoriesAsync(model, cancellationToken);
                return View("Form", model);
            }
            if (model.Image != null)
            {
                try { newImage = await images.SaveAsync(model.Image, cancellationToken); }
                catch (InvalidDataException exception)
                {
                    ModelState.AddModelError(nameof(model.Image), exception.Message);
                    await LoadCategoriesAsync(model, cancellationToken);
                    return View("Form", model);
                }
            }
            if (product == null)
            {
                product = new Product { CreatedAtUtc = DateTime.UtcNow };
                db.Products.Add(product);
            }
            oldImage = product.ImagePath;
            product.Name = model.Name.Trim();
            product.Description = model.Description.Trim();
            product.Unit = model.Unit.Trim();
            product.CategoryId = model.CategoryId;
            product.Price = model.Price;
            product.Stock = model.Stock;
            product.IsActive = model.IsActive;
            product.IsFeatured = model.IsFeatured;
            product.Version = Guid.NewGuid().ToString("N");
            if (newImage != null) product.ImagePath = newImage;
            await db.SaveChangesAsync(cancellationToken);
            // The new file now belongs to the committed row. Failure to remove an
            // obsolete file must not turn a successful database write into a retry.
            newImage = null;
            if (model.Image != null) TryDelete(oldImage);
            TempData["SuccessMessage"] = id.HasValue ? "Đã cập nhật sản phẩm." : "Đã thêm sản phẩm.";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException)
        {
            TryDelete(newImage);
            return Changed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A server acknowledgement may be lost after the row was committed.
            // Keep the file on uncertain outcomes; an orphan is preferable to
            // deleting an image that a successfully committed product references.
            throw;
        }
        catch (Exception)
        {
            // Generic I/O/database failures do not prove that commit rolled back.
            // Only the explicit concurrency failure above safely removes the file.
            logger.LogWarning("Không thể lưu sản phẩm.");
            ModelState.AddModelError("", "Chưa xác nhận được kết quả lưu sản phẩm. Hãy kiểm tra danh sách trước khi thử lại.");
            try { await LoadCategoriesAsync(model, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { model.Categories = []; }
            return View("Form", model);
        }
    }

    private async Task LoadCategoriesAsync(CatalogAdminProductForm model, CancellationToken cancellationToken)
    {
        model.Categories = await db.Categories.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(cancellationToken);
    }

    private IActionResult Changed()
    {
        TempData["ErrorMessage"] = "Sản phẩm hoặc tồn kho đã thay đổi ở phiên khác. Vui lòng mở lại trước khi chỉnh sửa.";
        return RedirectToAction(nameof(Index));
    }

    private void TryDelete(string? path)
    {
        try { images.DeleteGenerated(path); }
        catch (Exception) { logger.LogWarning("Chưa dọn được ảnh sản phẩm không còn sử dụng."); }
    }
}
