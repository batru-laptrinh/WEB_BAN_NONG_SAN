using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.ViewModels;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DashboardController(
    ApplicationDbContext db,
    ILookupNormalizer normalizer,
    ILogger<DashboardController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        int? customerCount = null;
        try
        {
            var customerRoleName = normalizer.NormalizeName("Customer");
            // Count real Customer memberships, including users who also have Admin.
            // Read only a scalar; never materialize account/password/token data.
            customerCount = await (
                from user in db.Users.AsNoTracking()
                join membership in db.UserRoles.AsNoTracking() on user.Id equals membership.UserId
                join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                where role.NormalizedName == customerRoleName
                select user.Id)
                .Distinct()
                .CountAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not expose provider exceptions or report a failed query as zero users.
            logger.LogWarning("Không thể đọc số tài khoản Customer cho Dashboard quản trị.");
        }

        int? productCount = null, orderCount = null, pendingCount = null, completedCount = null;
        decimal? revenue = null;
        try
        {
            productCount = await db.Products.CountAsync(cancellationToken);
            orderCount = await db.Orders.CountAsync(cancellationToken);
            pendingCount = await db.Orders.CountAsync(x => x.Status == OrderStatus.Pending, cancellationToken);
            completedCount = await db.Orders.CountAsync(x => x.Status == OrderStatus.Completed, cancellationToken);
            var completed = db.Orders.Where(x => x.Status == OrderStatus.Completed && x.PaymentStatus == PaymentStatus.Paid);
            revenue = db.Database.IsSqlServer()
                ? await completed.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0m
                : (await completed.Select(x => x.Total).ToListAsync(cancellationToken)).Sum();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            productCount = orderCount = pendingCount = completedCount = null;
            logger.LogWarning("Không thể đọc thống kê bán hàng cho Dashboard.");
        }

        return View(new AdminDashboardViewModel
        {
            CustomerCount = customerCount,
            ProductCount = productCount,
            OrderCount = orderCount,
            PendingOrderCount = pendingCount,
            CompletedOrderCount = completedCount,
            CompletedRevenue = revenue,
            CheckedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
