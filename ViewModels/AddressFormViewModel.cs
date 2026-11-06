using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class AddressFormViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên người nhận.")]
    [StringLength(100, ErrorMessage = "Họ tên người nhận tối đa 100 ký tự.")]
    [Display(Name = "Họ tên người nhận")]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại nhận hàng.")]
    [StringLength(20, MinimumLength = 8, ErrorMessage = "Số điện thoại cần từ 8 đến 20 ký tự.")]
    [RegularExpression(@"(?=(?:[^0-9]*[0-9]){8,15}[^0-9]*$)\+?[0-9][0-9 ()\-\.]*[0-9]", ErrorMessage = "Số điện thoại không hợp lệ.")]
    [Display(Name = "Số điện thoại nhận hàng")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tỉnh/thành phố.")]
    [StringLength(100, ErrorMessage = "Tỉnh/thành phố tối đa 100 ký tự.")]
    [Display(Name = "Tỉnh/thành phố")]
    public string Province { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập phường/xã.")]
    [StringLength(100, ErrorMessage = "Phường/xã tối đa 100 ký tự.")]
    [Display(Name = "Phường/xã")]
    public string Ward { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Địa danh trung gian tối đa 100 ký tự.")]
    [Display(Name = "Quận/huyện hoặc địa danh trung gian (tùy chọn)")]
    public string? District { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ chi tiết.")]
    [StringLength(250, ErrorMessage = "Địa chỉ chi tiết tối đa 250 ký tự.")]
    [Display(Name = "Số nhà, đường, thôn/xóm và thông tin chi tiết")]
    public string AddressLine { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Ghi chú giao hàng tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú giao hàng (tùy chọn)")]
    public string? DeliveryNote { get; set; }

    [StringLength(50, ErrorMessage = "Nhãn địa chỉ tối đa 50 ký tự.")]
    [Display(Name = "Nhãn địa chỉ (tùy chọn)")]
    public string? Label { get; set; }

    [Display(Name = "Đặt làm địa chỉ mặc định")]
    public bool IsDefault { get; set; }

    [StringLength(8192, ErrorMessage = "Xác nhận không hợp lệ. Hãy tải lại trang.")]
    public string? VersionToken { get; set; }

    [BindNever]
    public bool IsCurrentDefault { get; set; }
}
