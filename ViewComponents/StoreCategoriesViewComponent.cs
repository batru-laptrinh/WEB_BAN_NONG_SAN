using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.ViewComponents;

// Presentation-only active category navigation. Cache within this HTTP request,
// so header and footer use the same read without adding duplicate queries.
public sealed class StoreCategoriesViewComponent(ApplicationDbContext db,
    ILogger<StoreCategoriesViewComponent> logger) : ViewComponent
{
    private static readonly object RequestKey = new();
    public async Task<IViewComponentResult> InvokeAsync(string variant = "header")
    {
        if (HttpContext.Items[RequestKey] is not IReadOnlyList<CatalogCategory> categories)
        {
            try
            {
                categories = await db.Categories.AsNoTracking().Where(x => x.IsActive)
                    .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(12)
                    .Select(x => new CatalogCategory(x.Id, x.Name)).ToListAsync(HttpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                logger.LogWarning("Chưa tải được danh mục cho thanh điều hướng cửa hàng.");
                categories = Array.Empty<CatalogCategory>();
            }
            HttpContext.Items[RequestKey] = categories;
        }
        return View(variant == "footer" ? "Footer" : "Default", categories);
    }
}
