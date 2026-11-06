using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class OrderListViewModel
{
    public IReadOnlyList<OrderSummaryViewModel> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize => 20;
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public bool IsDataAvailable { get; init; } = true;
}

public sealed class OrderSummaryViewModel
{
    public Guid Id { get; init; }
    public string Code => Id.ToString("N").ToUpperInvariant();
    public DateTime CreatedAtUtc { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string RecipientName { get; init; } = string.Empty;
    public decimal Total { get; init; }
}

public sealed class OrderDetailsViewModel
{
    public Guid Id { get; init; }
    public string Code => Id.ToString("N").ToUpperInvariant();
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string RecipientName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Province { get; init; } = string.Empty;
    public string Ward { get; init; } = string.Empty;
    public string? District { get; init; }
    public string AddressLine { get; init; } = string.Empty;
    public string? DeliveryNote { get; init; }
    public decimal Subtotal { get; init; }
    public decimal ShippingFee { get; init; }
    public decimal Discount { get; init; }
    public decimal Total { get; init; }
    public string? CouponCode { get; init; }
    public IReadOnlyList<OrderLineViewModel> Items { get; init; } = [];
    public IReadOnlyList<OrderActionViewModel> Actions { get; init; } = [];
}

public sealed record OrderLineViewModel(string ProductName, string Unit, decimal UnitPrice,
    int Quantity, decimal LineTotal);

public sealed record OrderActionViewModel(OrderStatus TargetStatus, string Label, string Confirmation);

public static class OrderDisplay
{
    public static string Status(OrderStatus value) => value switch
    {
        OrderStatus.Pending => "Chờ xác nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Shipping => "Đang giao hàng",
        OrderStatus.Completed => "Hoàn thành",
        OrderStatus.Cancelled => "Đã hủy",
        _ => "Trạng thái không hợp lệ"
    };

    public static string Payment(PaymentStatus value) => value switch
    {
        PaymentStatus.Unpaid => "Chưa thu tiền · COD",
        PaymentStatus.Paid => "Đã thu tiền · COD",
        _ => "Chưa xác định"
    };
}
