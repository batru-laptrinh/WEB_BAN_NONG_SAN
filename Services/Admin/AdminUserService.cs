using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Services.Admin;

public enum AdminUserOperationStatus
{
    Success, NotFound, Forbidden, InvalidConfirmation, Conflict, Failure
}

public sealed record AdminUserOperationResult(AdminUserOperationStatus Status, string Message);

public sealed class AdminUserService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ILookupNormalizer normalizer,
    IDataProtectionProvider protectionProvider,
    ILogger<AdminUserService> logger)
{
    private const int PageSize = 10;
    private const int MaximumSearchLength = 100;
    private const string AdminRole = "Admin";
    private const string CustomerRole = "Customer";
    private readonly ITimeLimitedDataProtector confirmationProtector = protectionProvider
        .CreateProtector("WEB_BAN_NONG_SAN.AdminUsers.LockoutConfirmation.v1")
        .ToTimeLimitedDataProtector();

    public async Task<AdminUserListViewModel> GetListAsync(
        string? search, int page, string actorId, CancellationToken ct = default)
    {
        var filter = NormalizeSearch(search);
        var query = CustomerUsers();
        if (filter is not null)
        {
            var upperFilter = filter.ToUpperInvariant();
            query = query.Where(user =>
                (user.FullName != null && user.FullName.ToUpper().Contains(upperFilter))
                || (user.Email != null && user.Email.ToUpper().Contains(upperFilter))
                || (user.PhoneNumber != null && user.PhoneNumber.ToUpper().Contains(upperFilter)));
        }

        var totalCount = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);
        // Only the requested page and its role names are materialized. Internal
        // Identity fields are deliberately absent from this read projection.
        var rows = await query.OrderBy(user => user.NormalizedEmail).ThenBy(user => user.Id)
            .Skip((currentPage - 1) * PageSize).Take(PageSize)
            .Select(user => new UserRow
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd
            }).ToListAsync(ct);
        var roles = await GetRolesAsync(rows.Select(row => row.Id).ToArray(), ct);
        var now = DateTimeOffset.UtcNow;

        return new AdminUserListViewModel
        {
            Items = rows.Select(row => ToDetails(row, roles, actorId, now)).ToArray(),
            Search = filter,
            Page = currentPage,
            TotalCount = totalCount
        };
    }

    public async Task<AdminUserDetailsViewModel?> GetDetailsAsync(
        string id, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var row = await CustomerUsers().Where(user => user.Id == id)
            .Select(user => new UserRow
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd
            }).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;

        var roles = await GetRolesAsync([id], ct);
        return ToDetails(row, roles, actorId, DateTimeOffset.UtcNow);
    }

    public async Task<AdminUserConfirmationViewModel?> PrepareAsync(
        string id, string operation, string actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(actorId)
            || !IsOperation(operation))
            return null;

        // Displayed account data and its version come from the same query. The
        // version remains inside the protected token and never enters a view DTO.
        var pending = await CustomerUsers().Where(user => user.Id == id)
            .Select(user => new
            {
                Row = new UserRow
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber,
                    LockoutEnabled = user.LockoutEnabled,
                    LockoutEnd = user.LockoutEnd
                },
                Version = user.ConcurrencyStamp
            }).SingleOrDefaultAsync(ct);
        if (pending is null)
            return null;

        var roles = await GetRolesAsync([id], ct);
        var details = ToDetails(pending.Row, roles, actorId, DateTimeOffset.UtcNow);
        if ((operation == "lock" && !details.CanLock)
            || (operation == "unlock" && !details.CanUnlock))
            return null;

        var ticket = new ConfirmationTicket(id, actorId, operation, pending.Version);
        return new AdminUserConfirmationViewModel
        {
            User = details,
            Operation = operation,
            Confirmation = confirmationProtector.Protect(
                JsonSerializer.Serialize(ticket), TimeSpan.FromMinutes(10))
        };
    }

    public async Task<AdminUserOperationResult> ChangeLockoutAsync(
        string id, string operation, string confirmation, string actorId, CancellationToken ct = default)
    {
        if (!TryReadConfirmation(id, operation, confirmation, actorId, out var ticket))
            return InvalidConfirmation();

        ApplicationUser? target = null;
        try
        {
            // No transaction is held while a human reviews the confirmation.
            // Serializable holds SQL Server row/range locks across the current
            // actor/target/role checks and every Identity write. Concurrent role
            // promotion must finish before these checks, or wait until commit.
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            if (!await db.Users.AsNoTracking().AnyAsync(user => user.Id == actorId, ct)
                || !await HasRoleAsync(actorId, AdminRole, ct))
                return Forbidden();

            target = await users.FindByIdAsync(id);
            if (target is null)
                return NotFound();

            // FindByIdAsync can return an already tracked instance. Reload it so
            // state/version checks always use the row inside this transaction.
            await db.Entry(target).ReloadAsync(ct);
            if (db.Entry(target).State == EntityState.Detached)
                return NotFound();
            if (string.Equals(target.Id, actorId, StringComparison.Ordinal)
                || await HasRoleAsync(target.Id, AdminRole, ct))
                return Forbidden();
            if (!await HasRoleAsync(target.Id, CustomerRole, ct))
                return NotFound();

            if (!string.Equals(target.ConcurrencyStamp, ticket!.Version, StringComparison.Ordinal))
                return Conflict();

            var state = GetState(target.LockoutEnabled, target.LockoutEnd, DateTimeOffset.UtcNow);
            if ((operation == "lock" && state == AdminUserLockoutState.Manual)
                || (operation == "unlock" && state == AdminUserLockoutState.Active))
                return Conflict();

            IdentityResult result;
            if (operation == "lock")
            {
                if (!target.LockoutEnabled)
                {
                    result = await users.SetLockoutEnabledAsync(target, true);
                    if (!result.Succeeded)
                        return await RollbackFailureAsync(transaction, target, result, ct);
                }

                result = await users.SetLockoutEndDateAsync(target, DateTimeOffset.MaxValue);
                if (!result.Succeeded)
                    return await RollbackFailureAsync(transaction, target, result, ct);
            }
            else
            {
                result = await users.SetLockoutEndDateAsync(target, null);
                if (!result.Succeeded)
                    return await RollbackFailureAsync(transaction, target, result, ct);
                result = await users.ResetAccessFailedCountAsync(target);
                if (!result.Succeeded)
                    return await RollbackFailureAsync(transaction, target, result, ct);
            }

            // Existing cookies are rejected at the configured Identity security
            // stamp validation interval, not necessarily on the next request.
            result = await users.UpdateSecurityStampAsync(target);
            if (!result.Succeeded)
                return await RollbackFailureAsync(transaction, target, result, ct);

            await transaction.CommitAsync(ct);
            return new AdminUserOperationResult(AdminUserOperationStatus.Success,
                operation == "lock" ? "Đã khóa tài khoản khách hàng." : "Đã mở khóa tài khoản khách hàng.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Detach(target);
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(target);
            logger.LogWarning("Admin customer lockout operation encountered a concurrent change.");
            return Conflict();
        }
        catch (Exception)
        {
            Detach(target);
            // Do not log raw exceptions, Identity error descriptions, account
            // identifiers, confirmation tokens or connection-string contents.
            logger.LogWarning("Admin customer lockout operation could not confirm its outcome.");
            return new AdminUserOperationResult(AdminUserOperationStatus.Failure,
                "Không xác nhận được kết quả. Hãy tải lại trạng thái tài khoản trước khi thử lại.");
        }
    }

    private IQueryable<ApplicationUser> CustomerUsers()
    {
        var roleName = normalizer.NormalizeName(CustomerRole);
        return db.Users.AsNoTracking().Where(user => db.UserRoles.Any(link =>
            link.UserId == user.Id && db.Roles.Any(role =>
                role.Id == link.RoleId && role.NormalizedName == roleName)));
    }

    private Task<bool> HasRoleAsync(string userId, string roleName, CancellationToken ct)
    {
        var normalizedRole = normalizer.NormalizeName(roleName);
        return (from link in db.UserRoles.AsNoTracking()
                join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
                where link.UserId == userId && role.NormalizedName == normalizedRole
                select link.UserId).AnyAsync(ct);
    }

    private async Task<IReadOnlyList<RoleRow>> GetRolesAsync(string[] userIds, CancellationToken ct)
    {
        if (userIds.Length == 0)
            return [];

        return await (from link in db.UserRoles.AsNoTracking()
                      join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
                      where userIds.Contains(link.UserId)
                      select new RoleRow
                      {
                          UserId = link.UserId,
                          Name = role.Name,
                          NormalizedName = role.NormalizedName
                      }).ToListAsync(ct);
    }

    private AdminUserDetailsViewModel ToDetails(
        UserRow row, IReadOnlyList<RoleRow> roles, string actorId, DateTimeOffset now)
    {
        var userRoles = roles.Where(role => role.UserId == row.Id).ToArray();
        var normalizedAdmin = normalizer.NormalizeName(AdminRole);
        return new AdminUserDetailsViewModel
        {
            Id = row.Id,
            FullName = row.FullName ?? string.Empty,
            Email = row.Email ?? string.Empty,
            PhoneNumber = row.PhoneNumber ?? string.Empty,
            Roles = userRoles.Where(role => role.Name is not null)
                .Select(role => role.Name!).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            LockoutState = GetState(row.LockoutEnabled, row.LockoutEnd, now),
            LockoutEnd = row.LockoutEnd,
            IsAdmin = userRoles.Any(role => role.NormalizedName == normalizedAdmin),
            IsSelf = string.Equals(row.Id, actorId, StringComparison.Ordinal)
        };
    }

    private static AdminUserLockoutState GetState(
        bool lockoutEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset now)
    {
        if (!lockoutEnabled || lockoutEnd is null || lockoutEnd <= now)
            return AdminUserLockoutState.Active;
        return lockoutEnd == DateTimeOffset.MaxValue
            ? AdminUserLockoutState.Manual : AdminUserLockoutState.Temporary;
    }

    private bool TryReadConfirmation(
        string id, string operation, string confirmation, string actorId, out ConfirmationTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(actorId)
            || !IsOperation(operation) || string.IsNullOrWhiteSpace(confirmation)
            || confirmation.Length > 8192)
            return false;

        try
        {
            ticket = JsonSerializer.Deserialize<ConfirmationTicket>(confirmationProtector.Unprotect(confirmation));
            return ticket is not null
                && string.Equals(ticket.UserId, id, StringComparison.Ordinal)
                && string.Equals(ticket.ActorId, actorId, StringComparison.Ordinal)
                && string.Equals(ticket.Operation, operation, StringComparison.Ordinal);
        }
        catch (Exception error) when (error is CryptographicException or FormatException
            or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<AdminUserOperationResult> RollbackFailureAsync(
        IDbContextTransaction transaction, ApplicationUser target, IdentityResult result, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        Detach(target);
        if (result.Errors.Any(error => error.Code == users.ErrorDescriber.ConcurrencyFailure().Code))
            return Conflict();

        logger.LogWarning("Admin customer lockout operation was rolled back after an Identity failure.");
        return new AdminUserOperationResult(AdminUserOperationStatus.Failure,
            "Thao tác không thành công; các thay đổi đã được hủy. Hãy tải lại và thử lại.");
    }

    private void Detach(ApplicationUser? target)
    {
        if (target is not null)
            db.Entry(target).State = EntityState.Detached;
    }

    private static string? NormalizeSearch(string? search)
    {
        var value = search?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        return value.Length <= MaximumSearchLength ? value : value[..MaximumSearchLength];
    }

    private static bool IsOperation(string operation) => operation is "lock" or "unlock";
    private static AdminUserOperationResult InvalidConfirmation() => new(
        AdminUserOperationStatus.InvalidConfirmation,
        "Xác nhận không hợp lệ hoặc đã hết hạn. Hãy mở lại trang xác nhận.");
    private static AdminUserOperationResult NotFound() => new(
        AdminUserOperationStatus.NotFound, "Không tìm thấy tài khoản khách hàng trong phạm vi quản lý.");
    private static AdminUserOperationResult Forbidden() => new(
        AdminUserOperationStatus.Forbidden, "Không được phép thay đổi trạng thái tài khoản này.");
    private static AdminUserOperationResult Conflict() => new(
        AdminUserOperationStatus.Conflict,
        "Thông tin hoặc trạng thái tài khoản đã thay đổi. Hãy tải lại và xác nhận lại.");

    private sealed record ConfirmationTicket(string UserId, string ActorId, string Operation, string? Version);

    private sealed class UserRow
    {
        public required string Id { get; init; }
        public string? FullName { get; init; }
        public string? Email { get; init; }
        public string? PhoneNumber { get; init; }
        public bool LockoutEnabled { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
    }

    private sealed class RoleRow
    {
        public required string UserId { get; init; }
        public string? Name { get; init; }
        public string? NormalizedName { get; init; }
    }
}
