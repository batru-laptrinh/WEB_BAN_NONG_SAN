
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;
using WEB_BAN_NONG_SAN.Services.Email;

namespace WEB_BAN_NONG_SAN.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        // Hiển thị trang đăng ký
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        // Xử lý đăng ký tài khoản
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Vai trò phải được tạo sẵn bởi hệ thống
            if (!await _roleManager.RoleExistsAsync("Customer"))
            {
                ModelState.AddModelError(
                    "",
                    "Hệ thống chưa khởi tạo vai trò Customer.");

                return View(model);
            }

            var email = model.Email.Trim();

            var user = new ApplicationUser
            {
                FullName = model.FullName.Trim(),
                UserName = email,
                Email = email
            };

            var result = await _userManager.CreateAsync(
                user, model.Password);

            if (result.Succeeded)
            {
                // Khách đăng ký chỉ được nhận vai trò Customer
                var roleResult = await _userManager.AddToRoleAsync(
                    user, "Customer");

                if (!roleResult.Succeeded)
                {
                    // Không giữ tài khoản chưa được gán vai trò
                    await _userManager.DeleteAsync(user);

                    ModelState.AddModelError(
                        "",
                        "Không thể hoàn tất đăng ký. Vui lòng thử lại.");

                    return View(model);
                }

                // Tự động đăng nhập sau khi đăng ký
                await _signInManager.SignInAsync(
                    user, isPersistent: false);

                return RedirectToAction("Index", "Home");
            }

            // Hiển thị các lỗi đăng ký
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            return View(model);
        }

        // Hiển thị trang đăng nhập
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // Xử lý đăng nhập
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            LoginViewModel model,
            string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Tìm tài khoản theo email
            var user = await _userManager.FindByEmailAsync(
                model.Email.Trim());

            if (user == null)
            {
                ModelState.AddModelError(
                    "",
                    "Email hoặc mật khẩu không chính xác.");

                return View(model);
            }

            // Kiểm tra mật khẩu và đăng nhập
            var result = await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // Chỉ chuyển hướng đến đường dẫn nội bộ an toàn
                if (!string.IsNullOrEmpty(returnUrl)
                    && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(
                    "",
                    "Tài khoản đang bị khóa. Vui lòng thử lại sau hoặc liên hệ quản trị viên.");

                return View(model);
            }

            if (result.IsNotAllowed)
            {
                ModelState.AddModelError(
                    "",
                    "Tài khoản chưa được phép đăng nhập.");

                return View(model);
            }

            if (result.RequiresTwoFactor)
            {
                ModelState.AddModelError(
                    "",
                    "Tài khoản yêu cầu xác thực hai bước, tính năng này chưa được cấu hình.");

                return View(model);
            }

            ModelState.AddModelError(
                "",
                "Email hoặc mật khẩu không chính xác.");

            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();

            return RedirectToAction("Index", "Home");
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // Hiển thị trang đổi mật khẩu
        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        // Xử lý đổi mật khẩu
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Lấy tài khoản đang đăng nhập
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            // Cookie cũ có thể còn trong chu kỳ kiểm tra stamp. Không cho tài khoản
            // đang khóa đổi mật khẩu rồi làm mới cookie bằng stamp hiện tại.
            if (await _userManager.IsLockedOutAsync(user))
            {
                await _signInManager.SignOutAsync();
                return Challenge();
            }

            // Kiểm tra mật khẩu cũ và cập nhật mật khẩu mới
            var result = await _userManager.ChangePasswordAsync(
                user,
                model.CurrentPassword,
                model.NewPassword);

            if (result.Succeeded)
            {
                // Làm mới phiên đăng nhập sau khi đổi mật khẩu
                await _signInManager.RefreshSignInAsync(user);

                TempData["SuccessMessage"] =
                    "Đổi mật khẩu thành công!";

                return RedirectToAction(nameof(ChangePassword));
            }

            // Hiển thị lỗi khi đổi mật khẩu thất bại
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    "",
                    error.Description);
            }

            return View(model);
        }

        // Hiển thị trang quên mật khẩu
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordViewModel());
        }

        // Nhận yêu cầu quên mật khẩu và gửi liên kết qua Gmail SMTP
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(
            ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = model.Email.Trim();
            var user = await _userManager.FindByEmailAsync(email);

            // Không tiết lộ email có tồn tại trong hệ thống hay không.
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                // Dùng URL gốc đã cấu hình, không tạo liên kết
                // nhạy cảm trực tiếp từ Host header của yêu cầu.
                var publicBaseUrl = _configuration["App:PublicBaseUrl"];
                if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri) ||
                    (baseUri.Scheme != Uri.UriSchemeHttps &&
                     !(baseUri.IsLoopback && baseUri.Scheme == Uri.UriSchemeHttp)))
                {
                    throw new InvalidOperationException(
                        "Thiếu App:PublicBaseUrl hoặc địa chỉ không an toàn. " +
                        "Dùng HTTPS; chỉ localhost mới được dùng HTTP khi phát triển.");
                }

                // Token do ASP.NET Core Identity tự tạo và xác minh.
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                var relativeUrl = Url.Action(
                    nameof(ResetPassword),
                    "Account",
                    new { email = user.Email, token });

                if (string.IsNullOrWhiteSpace(relativeUrl))
                {
                    throw new InvalidOperationException(
                        "Không thể tạo đường dẫn đặt lại mật khẩu.");
                }

                var resetLink = new Uri(baseUri, relativeUrl).AbsoluteUri;
                var safeLink = System.Net.WebUtility.HtmlEncode(resetLink);

                var htmlMessage = $@"
                    <h2>Khôi phục mật khẩu - Nông Sản Sạch</h2>
                    <p>Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu của bạn.</p>
                    <p><a href=""{safeLink}"">Đặt lại mật khẩu</a></p>
                    <p>Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>";

                try
                {
                    await _emailService.SendEmailAsync(
                        user.Email,
                        "Khôi phục mật khẩu - Nông Sản Sạch",
                        htmlMessage);
                }
                catch (Exception)
                {
                    // Chỉ ghi lỗi kỹ thuật vào log, không lộ thông tin
                    // tài khoản/token hoặc lỗi SMTP cho người dùng.
                    // SMTP/provider exception text can contain recipient details,
                    // credentials or links. Record a fixed message only.
                    _logger.LogError("Không thể gửi email khôi phục mật khẩu.");
                }
            }

            // Trả lời giống nhau cho email tồn tại và không tồn tại.
            TempData["ForgotPasswordMessage"] =
                "Nếu email đã đăng ký, vui lòng kiểm tra hộp thư " +
                "(kể cả Spam) để tìm liên kết khôi phục mật khẩu.";

            return RedirectToAction(nameof(ForgotPassword));
        }

        // Hiển thị trang đặt lại mật khẩu
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(
            string? email, string? token)
        {
            // Kiểm tra thông tin liên kết khôi phục
            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(
                    "Liên kết đặt lại mật khẩu không hợp lệ.");
            }

            var model = new ResetPasswordViewModel
            {
                Email = email.Trim(),
                Token = token
            };

            return View(model);
        }

        // Xử lý đặt lại mật khẩu
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(
            ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Tìm tài khoản bằng email
            var user = await _userManager.FindByEmailAsync(
                model.Email.Trim());

            // Không tiết lộ email có tồn tại hay không
            if (user == null)
            {
                return RedirectToAction(
                    nameof(ResetPasswordConfirmation));
            }

            // Xác minh token và đặt lại mật khẩu
            var result = await _userManager.ResetPasswordAsync(
                user,
                model.Token,
                model.NewPassword);

            if (result.Succeeded)
            {
                return RedirectToAction(
                    nameof(ResetPasswordConfirmation));
            }

            // Hiển thị lỗi nếu đặt lại thất bại
            foreach (var error in result.Errors)
            {
                if (error.Code == "InvalidToken")
                {
                    ModelState.AddModelError(
                        "",
                        "Liên kết khôi phục không hợp lệ " +
                        "hoặc đã hết hạn.");
                }
                else
                {
                    ModelState.AddModelError(
                        "",
                        error.Description);
                }
            }

            return View(model);
        }

        // Trang thông báo sau khi đặt lại mật khẩu
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }
    }
}
