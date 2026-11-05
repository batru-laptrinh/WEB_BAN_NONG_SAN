using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Services;

namespace WEB_BAN_NONG_SAN.Controllers;

[AllowAnonymous]
public sealed class ProductImagesController(ProductImageService images, ApplicationDbContext db) : Controller
{
    [HttpGet("/ProductImages/{fileName}")]
    public async Task<IActionResult> Image(string fileName, CancellationToken cancellationToken = default)
    {
        // An image can later be hidden by deactivating its product/category.
        // Do not let an earlier Admin/public response bypass that visibility check.
        Response.Headers.CacheControl = "private,no-store";
        var path = images.GetExistingPath(fileName);
        if (path == null) return NotFound();
        try
        {
            if (!User.IsInRole("Admin") && !await db.Products.AsNoTracking().AnyAsync(x =>
                    x.IsActive && x.Category != null && x.Category.IsActive &&
                    x.ImagePath == "/ProductImages/" + fileName, cancellationToken)) return NotFound();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(StatusCodes.Status503ServiceUnavailable); }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "image/png");
    }
}
