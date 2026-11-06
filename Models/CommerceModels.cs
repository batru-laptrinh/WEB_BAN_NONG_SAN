using System.ComponentModel.DataAnnotations;

namespace WEB_BAN_NONG_SAN.Models;

public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    [Required, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [Required, MaxLength(4000)] public string Description { get; set; } = "";
    [Required, MaxLength(30)] public string Unit { get; set; } = "kg";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    [MaxLength(200)] public string? ImagePath { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

public class ShippingAddress
{
    public int Id { get; set; }
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
    [Required, MaxLength(100)] public string RecipientName { get; set; } = "";
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = "";
    [Required, MaxLength(100)] public string Province { get; set; } = "";
    [Required, MaxLength(100)] public string Ward { get; set; } = "";
    [MaxLength(100)] public string? District { get; set; }
    [Required, MaxLength(250)] public string AddressLine { get; set; } = "";
    [MaxLength(500)] public string? DeliveryNote { get; set; }
    [MaxLength(50)] public string? Label { get; set; }
    public bool IsDefault { get; set; }
    [Required, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

public class CartItem
{
    public int Id { get; set; }
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    [Required, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

public enum OrderStatus { Pending, Confirmed, Shipping, Completed, Cancelled }
public enum PaymentStatus { Unpaid, Paid }
public enum CouponKind { FixedAmount, Percentage }

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
    public Guid CheckoutKey { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public int? CouponId { get; set; }
    [MaxLength(50)] public string? CouponCode { get; set; }
    [Required, MaxLength(100)] public string RecipientName { get; set; } = "";
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = "";
    [Required, MaxLength(100)] public string Province { get; set; } = "";
    [Required, MaxLength(100)] public string Ward { get; set; } = "";
    [MaxLength(100)] public string? District { get; set; }
    [Required, MaxLength(250)] public string AddressLine { get; set; } = "";
    [MaxLength(500)] public string? DeliveryNote { get; set; }
    [Required, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    [Required, MaxLength(150)] public string ProductName { get; set; } = "";
    [Required, MaxLength(30)] public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class Coupon
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public CouponKind Kind { get; set; }
    public decimal Value { get; set; }
    public decimal MinimumSubtotal { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int MaxUses { get; set; } = 1;
    public int UsedCount { get; set; }
    [Required, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}

public class CouponUsage
{
    public int Id { get; set; }
    public int CouponId { get; set; }
    public Guid OrderId { get; set; }
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
}
