using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Services;

public enum OrderOperationStatus { Success, NotFound, Forbidden, InvalidConfirmation, Conflict, Failure }
public sealed record OrderOperationResult(OrderOperationStatus Status, string Message);

public sealed class OrderService(ApplicationDbContext db, IDataProtectionProvider protection,
    ILogger<OrderService> logger)
{
    private readonly ITimeLimitedDataProtector protector = protection
        .CreateProtector("WEB_BAN_NONG_SAN.Orders.StatusChange.v1").ToTimeLimitedDataProtector();

    public Task<OrderListViewModel> GetCustomerListAsync(string ownerId, int page = 1,
        CancellationToken ct = default) => ReadListAsync(db.Orders.AsNoTracking()
            .Where(order => order.UserId == ownerId), page, null, null, ct);

    public async Task<OrderListViewModel> GetAdminListAsync(string actorId, string? search,
        OrderStatus? status, int page = 1, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct)) return new() { IsDataAvailable = false };
        search = CleanSearch(search);
        if (status.HasValue && !Enum.IsDefined(status.Value)) status = null;
        var query = db.Orders.AsNoTracking();
        if (status.HasValue) query = query.Where(order => order.Status == status.Value);
        if (search is not null)
        {
            if (Guid.TryParse(search, out var orderId))
                query = query.Where(order => order.Id == orderId);
            else
            {
                var upper = search.ToUpperInvariant();
                query = query.Where(order => order.RecipientName.ToUpper().Contains(upper)
                    || order.PhoneNumber.Contains(search));
            }
        }
        return await ReadListAsync(query, page, search, status, ct);
    }

    public Task<OrderDetailsViewModel?> GetCustomerDetailsAsync(Guid id, string ownerId,
        CancellationToken ct = default) => ReadDetailsAsync(id, ownerId, false, ct);

    public async Task<OrderDetailsViewModel?> GetAdminDetailsAsync(Guid id, string actorId,
        CancellationToken ct = default) => await IsAdminAsync(actorId, ct)
            ? await ReadDetailsAsync(id, actorId, true, ct) : null;

    public Task<OrderOperationResult> CancelCustomerAsync(Guid id, string ownerId, string? confirmation,
        CancellationToken ct = default) => ChangeAsync(id, ownerId, false, OrderStatus.Cancelled,
            confirmation, ct);

    public Task<OrderOperationResult> ChangeAdminAsync(Guid id, string actorId, OrderStatus target,
        string? confirmation, CancellationToken ct = default) => ChangeAsync(id, actorId, true,
            target, confirmation, ct);

    private async Task<OrderListViewModel> ReadListAsync(IQueryable<Order> query, int page,
        string? search, OrderStatus? status, CancellationToken ct)
    {
        const int size = 20;
        var count = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)size)));
        var rows = await query.OrderByDescending(order => order.CreatedAtUtc).ThenBy(order => order.Id)
            .Skip((page - 1) * size).Take(size).Select(order => new OrderSummaryViewModel
            {
                Id = order.Id, CreatedAtUtc = order.CreatedAtUtc, Status = order.Status,
                PaymentStatus = order.PaymentStatus, RecipientName = order.RecipientName, Total = order.Total
            }).ToListAsync(ct);
        return new() { Items = rows, Page = page, TotalCount = count, Search = search, Status = status };
    }

    private async Task<OrderDetailsViewModel?> ReadDetailsAsync(Guid id, string actorId, bool admin,
        CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(order => order.Id == id);
        if (!admin) query = query.Where(order => order.UserId == actorId);
        var order = await query.Include(order => order.Items).SingleOrDefaultAsync(ct);
        if (order is null) return null;
        var targets = AllowedTargets(order.Status, admin);
        return new()
        {
            Id = order.Id, CreatedAtUtc = order.CreatedAtUtc, CompletedAtUtc = order.CompletedAtUtc,
            Status = order.Status, PaymentStatus = order.PaymentStatus, RecipientName = order.RecipientName,
            PhoneNumber = order.PhoneNumber, Province = order.Province, Ward = order.Ward,
            District = order.District, AddressLine = order.AddressLine, DeliveryNote = order.DeliveryNote,
            Subtotal = order.Subtotal, ShippingFee = order.ShippingFee, Discount = order.Discount,
            Total = order.Total, CouponCode = order.CouponCode,
            Items = order.Items.OrderBy(item => item.Id).Select(item => new OrderLineViewModel(
                item.ProductName, item.Unit, item.UnitPrice, item.Quantity, item.LineTotal)).ToArray(),
            Actions = targets.Select(target => new OrderActionViewModel(target, ActionLabel(target),
                protector.Protect(JsonSerializer.Serialize(new Ticket(order.Id, actorId, admin,
                    target, order.Version)), TimeSpan.FromMinutes(30)))).ToArray()
        };
    }

    private async Task<OrderOperationResult> ChangeAsync(Guid id, string actorId, bool admin,
        OrderStatus target, string? confirmation, CancellationToken ct)
    {
        // Ownership is checked before inspecting a token so an ID from another
        // customer is indistinguishable from a missing order.
        try
        {
            var visible = await db.Orders.AsNoTracking().AnyAsync(order => order.Id == id
                && (admin || order.UserId == actorId), ct);
            if (!visible) return NotFound();
            if (!TryReadTicket(id, actorId, admin, target, confirmation, out var ticket))
                return new(OrderOperationStatus.InvalidConfirmation,
                    "Xác nhận không hợp lệ hoặc đã hết hạn. Vui lòng tải lại chi tiết đơn hàng.");
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var actor = await db.Users.AsNoTracking().Where(user => user.Id == actorId)
                .Select(user => new { user.LockoutEnabled, user.LockoutEnd }).SingleOrDefaultAsync(ct);
            if (actor is null || (actor.LockoutEnabled && actor.LockoutEnd > DateTimeOffset.UtcNow)
                || (admin && !await IsAdminAsync(actorId, ct)))
                return new(OrderOperationStatus.Forbidden, "Tài khoản không được phép thực hiện thao tác này.");

            DetachMutationEntities();
            var order = await db.Orders.Include(value => value.Items).SingleOrDefaultAsync(value => value.Id == id
                && (admin || value.UserId == actorId), ct);
            if (order is null) return NotFound();
            // Repeated cancellation is a no-op. The same protected request can
            // never restore stock for a second time.
            if (target == OrderStatus.Cancelled && order.Status == OrderStatus.Cancelled)
                return new(OrderOperationStatus.Success, "Đơn hàng đã được hủy trước đó; tồn kho không thay đổi thêm.");
            if (order.Version != ticket!.Version || order.PaymentStatus != PaymentStatus.Unpaid
                || !AllowedTargets(order.Status, admin).Contains(target))
                return Conflict();

            if (target == OrderStatus.Cancelled)
            {
                var quantities = order.Items.GroupBy(item => item.ProductId)
                    .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => (long)item.Quantity) })
                    .OrderBy(item => item.ProductId).ToArray();
                if (quantities.Length == 0) return Conflict();
                var ids = quantities.Select(item => item.ProductId).ToArray();
                var products = await db.Products.Where(product => ids.Contains(product.Id))
                    .OrderBy(product => product.Id).ToDictionaryAsync(product => product.Id, ct);
                foreach (var item in quantities)
                {
                    if (item.Quantity <= 0 || !products.TryGetValue(item.ProductId, out var product)
                        || product.Stock < 0 || item.Quantity > 1_000_000L - product.Stock)
                        return Conflict();
                }
                // Validate every row before mutating tracked stock, including
                // when this scoped service is called again after a refusal.
                foreach (var item in quantities)
                {
                    var product = products[item.ProductId];
                    product.Stock += checked((int)item.Quantity);
                    product.Version = Guid.NewGuid().ToString("N");
                }
                // Coupon usage remains consumed after cancellation. Releasing a
                // promotion would allow repeated checkout/cancel limit abuse.
            }
            order.Status = target;
            if (target == OrderStatus.Completed)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.CompletedAtUtc = DateTime.UtcNow;
            }
            order.Version = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(OrderOperationStatus.Success,
                target == OrderStatus.Cancelled ? "Đã hủy đơn hàng và hoàn lại tồn kho một lần." : "Đã cập nhật trạng thái đơn hàng.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DetachMutationEntities();
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachMutationEntities();
            return Conflict();
        }
        catch (Exception)
        {
            DetachMutationEntities();
            logger.LogWarning("Order state mutation could not confirm its outcome.");
            return new(OrderOperationStatus.Failure,
                "Chưa xác nhận được kết quả. Hãy tải lại trạng thái đơn hàng trước khi thử lại.");
        }
    }

    private Task<bool> IsAdminAsync(string actorId, CancellationToken ct) =>
        (from link in db.UserRoles.AsNoTracking()
         join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
         where link.UserId == actorId && role.NormalizedName == "ADMIN"
         select link.UserId).AnyAsync(ct);

    private bool TryReadTicket(Guid id, string actorId, bool admin, OrderStatus target,
        string? confirmation, out Ticket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(confirmation) || confirmation.Length > 8192
            || !Enum.IsDefined(target)) return false;
        try
        {
            ticket = JsonSerializer.Deserialize<Ticket>(protector.Unprotect(confirmation));
            return ticket is not null && ticket.OrderId == id && ticket.ActorId == actorId
                && ticket.Admin == admin && ticket.Target == target;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException
            or ArgumentException) { return false; }
    }

    private void DetachMutationEntities()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.Entity is Order or OrderItem or Product).ToArray())
            entry.State = EntityState.Detached;
    }

    private static OrderStatus[] AllowedTargets(OrderStatus status, bool admin) => (status, admin) switch
    {
        (OrderStatus.Pending, false) => [OrderStatus.Cancelled],
        (OrderStatus.Pending, true) => [OrderStatus.Confirmed, OrderStatus.Cancelled],
        (OrderStatus.Confirmed, true) => [OrderStatus.Shipping, OrderStatus.Cancelled],
        (OrderStatus.Shipping, true) => [OrderStatus.Completed],
        _ => []
    };

    private static string ActionLabel(OrderStatus target) => target switch
    {
        OrderStatus.Confirmed => "Xác nhận đơn hàng",
        OrderStatus.Shipping => "Bắt đầu giao hàng",
        OrderStatus.Completed => "Xác nhận đã giao và thu đủ tiền COD",
        OrderStatus.Cancelled => "Hủy đơn hàng",
        _ => "Cập nhật"
    };

    private static string? CleanSearch(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim()[..Math.Min(100, value.Trim().Length)];
    private static OrderOperationResult NotFound() => new(OrderOperationStatus.NotFound, "Không tìm thấy đơn hàng.");
    private static OrderOperationResult Conflict() => new(OrderOperationStatus.Conflict,
        "Đơn hàng hoặc tồn kho đã thay đổi. Vui lòng tải lại chi tiết và xác nhận lại.");
    private sealed record Ticket(Guid OrderId, string ActorId, bool Admin, OrderStatus Target, string Version);
}
