using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ReportsController(ApplicationDbContext db, ILookupNormalizer normalizer,
    ILogger<ReportsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        try
        {
            var today = now.AddHours(7).Date;
            var firstDay = today.AddDays(-29);
            var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
            var customerRole = normalizer.NormalizeName("Customer");
            var customerCount = await (from user in db.Users.AsNoTracking()
                join membership in db.UserRoles.AsNoTracking() on user.Id equals membership.UserId
                join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                where role.NormalizedName == customerRole
                select user.Id).Distinct().CountAsync(cancellationToken);

            var eligible = db.Orders.AsNoTracking().Where(x => x.Status == OrderStatus.Completed && x.PaymentStatus == PaymentStatus.Paid);
            var revenue = await eligible.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0m;
            var monthlyStart = DateTime.SpecifyKind(firstMonth.AddHours(-7), DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(today.AddDays(1).AddHours(-7), DateTimeKind.Utc);
            // Keep decimal arithmetic exact. Read only completed-order scalars for this
            // bounded date window, then group by Vietnam calendar day/month in memory.
            var periods = await eligible.Where(x => x.CompletedAtUtc >= monthlyStart && x.CompletedAtUtc < end)
                .Select(x => new { x.CompletedAtUtc, x.Total }).ToListAsync(cancellationToken);
            var days = periods.GroupBy(x => x.CompletedAtUtc!.Value.AddHours(7).Date)
                .ToDictionary(x => x.Key, x => new { Revenue = x.Sum(y => y.Total), Count = x.Count() });
            var months = periods.GroupBy(x => new { x.CompletedAtUtc!.Value.AddHours(7).Year, x.CompletedAtUtc.Value.AddHours(7).Month })
                .ToDictionary(x => (x.Key.Year, x.Key.Month), x => new { Revenue = x.Sum(y => y.Total), Count = x.Count() });
            var top = await db.OrderItems.AsNoTracking()
                .Where(x => x.Order!.Status == OrderStatus.Completed && x.Order.PaymentStatus == PaymentStatus.Paid)
                .GroupBy(x => new { x.ProductId, x.ProductName, x.Unit })
                .Select(x => new { x.Key.ProductId, x.Key.ProductName, x.Key.Unit, Quantity = x.Sum(y => (long)y.Quantity), GrossValue = x.Sum(y => y.LineTotal) })
                .OrderByDescending(x => x.Quantity).ThenBy(x => x.ProductId).ThenBy(x => x.ProductName)
                .Take(10).ToListAsync(cancellationToken);

            return View(new ReportsViewModel
            {
                CheckedAtUtc = now, CustomerCount = customerCount,
                ProductCount = await db.Products.CountAsync(cancellationToken),
                OrderCount = await db.Orders.CountAsync(cancellationToken),
                PendingOrderCount = await db.Orders.CountAsync(x => x.Status == OrderStatus.Pending, cancellationToken),
                CompletedOrderCount = await db.Orders.CountAsync(x => x.Status == OrderStatus.Completed, cancellationToken),
                Revenue = revenue,
                DailyRevenue = Enumerable.Range(0, 30).Select(offset => firstDay.AddDays(offset))
                    .Select(day => new RevenuePeriod(day.ToString("dd/MM"), days.TryGetValue(day, out var value) ? value.Revenue : 0m,
                        days.TryGetValue(day, out var count) ? count.Count : 0)).ToList(),
                MonthlyRevenue = Enumerable.Range(0, 12).Select(offset => firstMonth.AddMonths(offset))
                    .Select(month => new RevenuePeriod(month.ToString("MM/yyyy"), months.TryGetValue((month.Year, month.Month), out var value) ? value.Revenue : 0m,
                        months.TryGetValue((month.Year, month.Month), out var count) ? count.Count : 0)).ToList(),
                TopProducts = top.Select(x => new TopSellingProduct(x.ProductId, x.ProductName, x.Unit, x.Quantity, x.GrossValue)).ToList()
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Không thể đọc thống kê kinh doanh.");
            return View(new ReportsViewModel { CheckedAtUtc = now, IsDataAvailable = false });
        }
    }
}
