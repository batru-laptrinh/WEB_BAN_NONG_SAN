using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.ViewModels;

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

        return View(new AdminDashboardViewModel
        {
            CustomerCount = customerCount,
            CheckedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
