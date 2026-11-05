using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace WEB_BAN_NONG_SAN.Controllers;

public class HomeController(ApplicationDbContext db, ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var active = db.Products.AsNoTracking().Where(p => p.IsActive && p.Category!.IsActive);
            var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive)
                .OrderBy(c => c.Name).Take(12).Select(c => new CatalogCategory(c.Id, c.Name)).ToListAsync(cancellationToken);
            var featured = await active.Where(p => p.IsFeatured)
                .OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Take(4)
                .Select(p => new CatalogProductCard
                {
                    Id = p.Id, Name = p.Name, Price = p.Price, Stock = p.Stock, Unit = p.Unit,
                    CategoryName = p.Category!.Name, ImagePath = p.ImagePath
                }).ToListAsync(cancellationToken);
            var newest = await active.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Take(8)
                .Select(p => new CatalogProductCard
                {
                    Id = p.Id, Name = p.Name, Price = p.Price, Stock = p.Stock, Unit = p.Unit,
                    CategoryName = p.Category!.Name, ImagePath = p.ImagePath
                }).ToListAsync(cancellationToken);
            return View(new CatalogHomeViewModel { Categories = categories, FeaturedProducts = featured, NewProducts = newest });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Không thể tải sản phẩm trang chủ.");
            return View(new CatalogHomeViewModel { IsDataAvailable = false });
        }
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
