
using System.ComponentModel.DataAnnotations;

namespace WEB_BAN_NONG_SAN.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ")]
        [Display(Name = "Địa chỉ email")]
        public string Email { get; set; } = string.Empty;
    }
}
