using System.ComponentModel.DataAnnotations;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class CheckoutViewModel
{
    public CartViewModel Cart { get; init; } = new();
    public IReadOnlyList<CheckoutAddressViewModel> Addresses { get; init; } = [];
    public CheckoutInput Input { get; set; } = new();
    public decimal ShippingFee { get; init; }
    public decimal Discount { get; init; }
    public decimal Total => Cart.Subtotal + ShippingFee - Discount;
    public string? CouponMessage { get; init; }
    public bool IsAvailable { get; init; } = true;
    public bool CanPlaceOrder => IsAvailable && Cart.CanCheckout && Addresses.Count > 0;
}

public sealed class CheckoutAddressViewModel
{
    public int Id { get; init; }
    public string RecipientName { get; init; } = "";
    public string PhoneNumber { get; init; } = "";
    public string FullAddress { get; init; } = "";
    public bool IsDefault { get; init; }
}

public sealed class CheckoutInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn địa chỉ giao hàng.")]
    public int AddressId { get; set; }
    [StringLength(50, ErrorMessage = "Mã giảm giá tối đa 50 ký tự.")]
    public string? CouponCode { get; set; }
    [Required(ErrorMessage = "Phiên đặt hàng đã hết hạn. Vui lòng tải lại trang.")]
    public string CheckoutToken { get; set; } = "";
}
