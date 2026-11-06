using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Services;

public sealed record CommerceResult(bool Succeeded, string Message, Guid? OrderId = null, bool NotFound = false);

/// <summary>
/// All amounts and ownership come from the database. Serializable transactions and
/// concurrency tokens protect stock, cart, coupon allowance and order creation together.
/// This service never connects to an external payment or delivery provider.
/// </summary>
public sealed class CheckoutService(
    ApplicationDbContext db,
    IDataProtectionProvider protection,
    IConfiguration configuration,
    ILogger<CheckoutService> logger)
{
    private readonly ITimeLimitedDataProtector checkoutProtector = protection
        .CreateProtector("WEB_BAN_NONG_SAN.Checkout.v1").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector cartProtector = protection
        .CreateProtector("WEB_BAN_NONG_SAN.Cart.v1").ToTimeLimitedDataProtector();
    private const string RetryMessage = "Dữ liệu vừa thay đổi hoặc chưa thể lưu. Vui lòng tải lại trang và kiểm tra trước khi thử lại.";

    public async Task<CartViewModel> GetCartAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var items = await CartQuery(userId).AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
            return BuildCart(userId, items);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure("cart-read");
            return new CartViewModel { IsAvailable = false };
        }
    }

    public async Task<CommerceResult> AddAsync(string userId, int productId, int quantity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || quantity is < 1 or > 1000)
            return Fail("Số lượng phải từ 1 đến 1.000.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var product = await db.Products.Include(x => x.Category).SingleOrDefaultAsync(x => x.Id == productId, ct);
            if (product is null || !CanSell(product))
                return Fail("Sản phẩm không còn được bán.");
            var item = await db.CartItems.SingleOrDefaultAsync(x => x.UserId == userId && x.ProductId == productId, ct);
            var totalQuantity = (long)(item?.Quantity ?? 0) + quantity;
            if (totalQuantity > 1000 || totalQuantity > product.Stock)
                return Fail("Số lượng yêu cầu vượt tồn kho hoặc giới hạn 1.000 mỗi sản phẩm.");
            if (item is null)
            {
                if (await db.CartItems.CountAsync(x => x.UserId == userId, ct) >= 100)
                    return Fail("Giỏ hàng tối đa 100 sản phẩm khác nhau.");
                db.CartItems.Add(new CartItem { UserId = userId, ProductId = productId, Quantity = quantity, Version = NewVersion() });
            }
            else
            {
                item.Quantity = (int)totalQuantity;
                item.Version = NewVersion();
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok("Đã thêm sản phẩm vào giỏ hàng.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            LogFailure("cart-add");
            return Fail(RetryMessage);
        }
    }

    public Task<CommerceResult> UpdateAsync(string userId, int itemId, int quantity, string? token, CancellationToken ct = default) =>
        MutateCartAsync(userId, itemId, quantity, token, false, ct);

    public Task<CommerceResult> RemoveAsync(string userId, int itemId, string? token, CancellationToken ct = default) =>
        MutateCartAsync(userId, itemId, 0, token, true, ct);

    private async Task<CommerceResult> MutateCartAsync(string userId, int itemId, int quantity, string? token, bool remove, CancellationToken ct)
    {
        if (!remove && quantity is < 1 or > 1000) return Fail("Số lượng phải từ 1 đến 1.000.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var item = await CartQuery(userId).SingleOrDefaultAsync(x => x.Id == itemId, ct);
            if (item is null) return Missing();
            var payload = Unprotect<CartToken>(cartProtector, token);
            if (payload is null || payload.Owner != userId || payload.Id != item.Id || payload.Version != item.Version)
                return Fail(RetryMessage);
            if (remove) db.CartItems.Remove(item);
            else
            {
                if (item.Product is null || !CanSell(item.Product) || quantity > item.Product.Stock)
                    return Fail("Sản phẩm đã ngừng bán hoặc số lượng vượt tồn kho.");
                item.Quantity = quantity;
                item.Version = NewVersion();
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(remove ? "Đã xóa sản phẩm khỏi giỏ hàng." : "Đã cập nhật giỏ hàng.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            LogFailure("cart-change");
            return Fail(RetryMessage);
        }
    }

    public async Task<CheckoutViewModel> GetCheckoutAsync(string userId, string? couponCode = null, CancellationToken ct = default)
    {
        try
        {
            var cartItems = await CartQuery(userId).AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
            var cart = BuildCart(userId, cartItems);
            var addresses = await db.ShippingAddresses.AsNoTracking().Where(x => x.UserId == userId)
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id).ToListAsync(ct);
            var normalizedCode = NormalizeCode(couponCode);
            var coupon = normalizedCode is null ? null : await db.Coupons.AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalizedCode, ct);
            var couponResult = EvaluateCoupon(coupon, normalizedCode, cart.Subtotal);
            var shippingFee = GetShippingFee();
            var payload = new CheckoutToken(userId, Guid.NewGuid(), Fingerprint(cartItems),
                addresses.ToDictionary(x => x.Id, x => x.Version), normalizedCode, coupon?.Version,
                shippingFee, couponResult.Valid ? couponResult.Discount : 0);
            return new CheckoutViewModel
            {
                Cart = cart,
                Addresses = addresses.Select(x => new CheckoutAddressViewModel
                {
                    Id = x.Id, RecipientName = x.RecipientName, PhoneNumber = x.PhoneNumber, IsDefault = x.IsDefault,
                    FullAddress = string.Join(", ", new[] { x.AddressLine, x.Ward, x.District, x.Province }.Where(v => !string.IsNullOrWhiteSpace(v)))
                }).ToList(),
                Input = new CheckoutInput
                {
                    AddressId = addresses.FirstOrDefault()?.Id ?? 0,
                    CouponCode = normalizedCode,
                    CheckoutToken = checkoutProtector.Protect(JsonSerializer.Serialize(payload), TimeSpan.FromMinutes(30))
                },
                ShippingFee = shippingFee,
                Discount = couponResult.Valid ? couponResult.Discount : 0,
                CouponMessage = normalizedCode is null ? null : couponResult.Message
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure("checkout-read");
            return new CheckoutViewModel { IsAvailable = false };
        }
    }

    public async Task<CommerceResult> PlaceOrderAsync(string userId, CheckoutInput input, CancellationToken ct = default)
    {
        var payload = Unprotect<CheckoutToken>(checkoutProtector, input.CheckoutToken);
        if (payload is null || payload.Owner != userId || payload.Key == Guid.Empty)
            return Fail("Phiên đặt hàng không hợp lệ hoặc đã hết hạn. Vui lòng tải lại trang.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Retry of a committed request returns its own existing order, even though cart is now empty.
            var previous = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.CheckoutKey == payload.Key, ct);
            if (previous is not null) return new CommerceResult(true, "Đơn hàng này đã được ghi nhận.", previous.Id);
            var address = await db.ShippingAddresses.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == input.AddressId, ct);
            if (address is null || !payload.AddressVersions.TryGetValue(address.Id, out var addressVersion) || address.Version != addressVersion)
                return Fail("Địa chỉ không hợp lệ hoặc vừa thay đổi. Vui lòng chọn lại địa chỉ.");
            var cart = await CartQuery(userId).OrderBy(x => x.Id).ToListAsync(ct);
            if (cart.Count is < 1 or > 100 || Fingerprint(cart) != payload.Fingerprint)
                return Fail("Giỏ hàng hoặc giá sản phẩm vừa thay đổi. Vui lòng tải lại và kiểm tra đơn hàng.");
            if (cart.Any(x => x.Quantity is < 1 or > 1000 || x.Product is null || !CanSell(x.Product) || x.Quantity > x.Product.Stock))
                return Fail("Có sản phẩm ngừng bán hoặc không đủ tồn kho. Vui lòng kiểm tra giỏ hàng.");
            var subtotal = Money(cart.Sum(x => x.Product!.Price * x.Quantity));
            var code = NormalizeCode(input.CouponCode);
            if (input.CouponCode?.Length > 50) return Fail("Mã giảm giá không hợp lệ.");
            if (code != payload.CouponCode)
                return Fail("Mã giảm giá đã thay đổi. Vui lòng kiểm tra mã và xem lại tổng thanh toán trước khi đặt hàng.");
            var coupon = code is null ? null : await db.Coupons.SingleOrDefaultAsync(x => x.Code == code, ct);
            var couponResult = EvaluateCoupon(coupon, code, subtotal);
            if (!couponResult.Valid) return Fail(couponResult.Message);
            var shipping = GetShippingFee();
            if (coupon?.Version != payload.CouponVersion || couponResult.Discount != payload.Discount || shipping != payload.ShippingFee)
                return Fail("Điều kiện giảm giá hoặc phí vận chuyển vừa thay đổi. Vui lòng tải lại và xem tổng thanh toán mới.");
            var order = new Order
            {
                Id = Guid.NewGuid(), UserId = userId, CheckoutKey = payload.Key,
                Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Unpaid, CreatedAtUtc = DateTime.UtcNow,
                Subtotal = subtotal, ShippingFee = shipping, Discount = couponResult.Discount,
                Total = Money(subtotal + shipping - couponResult.Discount), CouponId = coupon?.Id, CouponCode = coupon?.Code,
                RecipientName = address.RecipientName, PhoneNumber = address.PhoneNumber, Province = address.Province,
                Ward = address.Ward, District = address.District, AddressLine = address.AddressLine, DeliveryNote = address.DeliveryNote,
                Version = NewVersion(),
                Items = cart.Select(x => new OrderItem
                {
                    ProductId = x.ProductId, ProductName = x.Product!.Name, Unit = x.Product.Unit,
                    UnitPrice = x.Product.Price, Quantity = x.Quantity, LineTotal = Money(x.Product.Price * x.Quantity)
                }).ToList()
            };
            foreach (var item in cart)
            {
                item.Product!.Stock -= item.Quantity;
                item.Product.Version = NewVersion();
            }
            if (coupon is not null)
            {
                coupon.UsedCount++;
                coupon.Version = NewVersion();
                db.CouponUsages.Add(new CouponUsage { CouponId = coupon.Id, OrderId = order.Id, UserId = userId });
            }
            db.Orders.Add(order);
            db.CartItems.RemoveRange(cart);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new CommerceResult(true, "Đặt hàng thành công. Thanh toán COD khi nhận hàng.", order.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // await using has rolled back before control reaches this handler.
            db.ChangeTracker.Clear();
            try
            {
                var committed = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.CheckoutKey == payload.Key, ct);
                if (committed is not null) return new CommerceResult(true, "Đơn hàng này đã được ghi nhận.", committed.Id);
            }
            catch (Exception retry) when (retry is not OperationCanceledException) { }
            LogFailure("checkout-write");
            return Fail(RetryMessage);
        }
    }

    private IQueryable<CartItem> CartQuery(string userId) => db.CartItems
        .Where(x => x.UserId == userId).Include(x => x.Product).ThenInclude(x => x!.Category);

    private CartViewModel BuildCart(string userId, IReadOnlyList<CartItem> items) => new()
    {
        Items = items.Select(x => new CartLineViewModel
        {
            Id = x.Id, ProductId = x.ProductId, Name = x.Product?.Name ?? "Sản phẩm không còn tồn tại",
            ImagePath = x.Product?.ImagePath,
            Unit = x.Product?.Unit ?? "", UnitPrice = x.Product?.Price ?? 0, Quantity = x.Quantity,
            Stock = x.Product?.Stock ?? 0,
            IsAvailable = x.Product is not null && CanSell(x.Product) && x.Quantity is >= 1 and <= 1000 && x.Quantity <= x.Product.Stock,
            MutationToken = cartProtector.Protect(JsonSerializer.Serialize(new CartToken(userId, x.Id, x.Version)), TimeSpan.FromMinutes(30))
        }).ToList(),
        Subtotal = Money(items.Sum(x => (x.Product?.Price ?? 0) * x.Quantity))
    };

    private decimal GetShippingFee()
    {
        var value = configuration.GetValue<decimal?>("Commerce:ShippingFee") ?? 30000m;
        if (value is < 0 or > 1000000 || Money(value) != value)
            throw new InvalidOperationException("Invalid shipping configuration.");
        return value;
    }

    private static (bool Valid, decimal Discount, string Message) EvaluateCoupon(Coupon? coupon, string? code, decimal subtotal)
    {
        if (code is null) return (true, 0, "");
        var now = DateTime.UtcNow;
        if (coupon is null || !coupon.IsActive || now < coupon.StartsAtUtc || now >= coupon.EndsAtUtc ||
            coupon.MaxUses < 1 || coupon.UsedCount >= coupon.MaxUses || coupon.UsedCount < 0 || subtotal < coupon.MinimumSubtotal ||
            coupon.Value <= 0 || coupon.Value > 1000000000m || Money(coupon.Value) != coupon.Value ||
            coupon.MinimumSubtotal < 0 || Money(coupon.MinimumSubtotal) != coupon.MinimumSubtotal ||
            !Enum.IsDefined(coupon.Kind) || (coupon.Kind == CouponKind.Percentage && coupon.Value > 100))
            return (false, 0, "Mã giảm giá không hợp lệ, hết lượt, ngoài thời hạn hoặc đơn hàng chưa đạt điều kiện.");
        var discount = coupon.Kind == CouponKind.Percentage ? Money(subtotal * coupon.Value / 100m) : Money(coupon.Value);
        return (true, Math.Min(subtotal, discount), "Mã giảm giá hợp lệ; điều kiện sẽ được kiểm tra lại khi đặt hàng.");
    }

    private static string Fingerprint(IEnumerable<CartItem> items) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(items.OrderBy(x => x.Id).Select(x => new
        {
            x.Id, x.ProductId, x.Quantity, x.Version,
            ProductVersion = x.Product?.Version, CategoryVersion = x.Product?.Category?.Version,
            ProductName = x.Product?.Name, ProductUnit = x.Product?.Unit, ProductPrice = x.Product?.Price, ProductActive = x.Product?.IsActive,
            CategoryActive = x.Product?.Category?.IsActive
        })))));

    private static T? Unprotect<T>(ITimeLimitedDataProtector protector, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32000) return default;
        try { return JsonSerializer.Deserialize<T>(protector.Unprotect(token, out _)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return default; }
    }

    private static bool CanSell(Product product) => product.IsActive && product.Category?.IsActive == true && product.Price >= 0 && product.Price <= 1000000000m && Money(product.Price) == product.Price && product.Stock >= 0;
    private static string? NormalizeCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string NewVersion() => Guid.NewGuid().ToString("N");
    private static CommerceResult Ok(string message) => new(true, message);
    private static CommerceResult Fail(string message) => new(false, message);
    private static CommerceResult Missing() => new(false, "Không tìm thấy mục giỏ hàng.", NotFound: true);
    private void LogFailure(string operation) => logger.LogWarning("Commerce operation failed: {Operation}. No customer or database details recorded.", operation);

    private sealed record CartToken(string Owner, int Id, string Version);
    private sealed record CheckoutToken(string Owner, Guid Key, string Fingerprint, Dictionary<int, string> AddressVersions,
        string? CouponCode, string? CouponVersion, decimal ShippingFee, decimal Discount);
}
