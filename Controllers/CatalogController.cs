using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers;

public sealed class CatalogController(ApplicationDbContext db, ILogger<CatalogController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CatalogQuery query, CancellationToken cancellationToken)
    {
        query.Q = query.Q?.Trim();
        query.Page = Math.Clamp(query.Page, 1, 1_000_000);
        query.Sort = query.Sort is "price-asc" or "price-desc" or "name" ? query.Sort : "newest";
        if (query.MinPrice > query.MaxPrice)
            ModelState.AddModelError("", "Giá thấp nhất không được lớn hơn giá cao nhất.");

        try
        {
            var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive)
                .OrderBy(c => c.Name).Select(c => new CatalogCategory(c.Id, c.Name)).ToListAsync(cancellationToken);
            if (!ModelState.IsValid)
                return View(new CatalogIndexViewModel { Query = query, Categories = categories });

            var products = db.Products.AsNoTracking().Where(p => p.IsActive && p.Category!.IsActive);
            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                var keyword = query.Q.ToUpperInvariant();
                products = products.Where(p => p.Name.ToUpper().Contains(keyword));
            }
            if (query.CategoryId.HasValue) products = products.Where(p => p.CategoryId == query.CategoryId);
            if (query.MinPrice.HasValue) products = products.Where(p => p.Price >= query.MinPrice);
            if (query.MaxPrice.HasValue) products = products.Where(p => p.Price <= query.MaxPrice);

            var count = await products.CountAsync(cancellationToken);
            query.Page = Math.Min(query.Page, Math.Max(1, (int)Math.Ceiling(count / 12d)));
            // SQL Server uses exact decimal ordering. SQLite's EF provider does not
            // support decimal ORDER BY; its test-only key uses exact integer cents.
            // Prices are constrained to decimal(18,2) and at most one billion.
            var sqlite = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
            var ordered = query.Sort switch
            {
                "price-asc" when sqlite => products.OrderBy(p => (long)(p.Price * 100m)).ThenBy(p => p.Id),
                "price-desc" when sqlite => products.OrderByDescending(p => (long)(p.Price * 100m)).ThenBy(p => p.Id),
                "price-asc" => products.OrderBy(p => p.Price).ThenBy(p => p.Id),
                "price-desc" => products.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
                "name" => products.OrderBy(p => p.Name).ThenBy(p => p.Id),
                _ => products.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id)
            };
            var items = await ordered.Skip((query.Page - 1) * 12).Take(12)
                .Select(p => new CatalogProductCard
                {
                    Id = p.Id, Name = p.Name, Unit = p.Unit, Price = p.Price, Stock = p.Stock,
                    CategoryName = p.Category!.Name, ImagePath = p.ImagePath
                }).ToListAsync(cancellationToken);
            return View(new CatalogIndexViewModel { Query = query, Categories = categories, Products = items, TotalCount = count });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Không thể tải danh sách sản phẩm.");
            return View(new CatalogIndexViewModel { Query = query, IsDataAvailable = false });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();
        try
        {
            var product = await db.Products.AsNoTracking()
                .Where(p => p.Id == id && p.IsActive && p.Category!.IsActive)
                .Select(p => new
                {
                    p.Id, p.Name, p.Unit, p.Price, p.Stock, p.Description, p.CategoryId,
                    CategoryName = p.Category!.Name, p.ImagePath
                }).SingleOrDefaultAsync(cancellationToken);
            if (product is null) return NotFound();
            var related = await db.Products.AsNoTracking()
                .Where(p => p.Id != id && p.CategoryId == product.CategoryId && p.IsActive && p.Category!.IsActive)
                .OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Take(4)
                .Select(p => new CatalogProductCard
                {
                    Id = p.Id, Name = p.Name, Unit = p.Unit, Price = p.Price, Stock = p.Stock,
                    CategoryName = p.Category!.Name, ImagePath = p.ImagePath
                }).ToListAsync(cancellationToken);
            return View(new CatalogDetailsViewModel
            {
                Product = new CatalogProductCard
                {
                    Id = product.Id, Name = product.Name, Unit = product.Unit, Price = product.Price,
                    Stock = product.Stock, CategoryName = product.CategoryName, ImagePath = product.ImagePath
                },
                Description = product.Description, RelatedProducts = related
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Không thể tải chi tiết sản phẩm.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Chưa thể tải sản phẩm. Vui lòng thử lại sau.");
        }
    }
}
