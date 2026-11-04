
using System.ComponentModel.DataAnnotations;

namespace WEB_BAN_NONG_SAN.ViewModels
{
    public class ResetPasswordViewModel
    {
        [Required(ErrorMessage = "Thiếu địa chỉ email")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Liên kết khôi phục không hợp lệ")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
        [StringLength(100, MinimumLength = 8,
            ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
        [DataType(DataType.Password)]
        [Compare("NewPassword",
            ErrorMessage = "Mật khẩu xác nhận không khớp")]
        [Display(Name = "Xác nhận mật khẩu mới")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
