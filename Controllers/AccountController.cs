
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
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
                    "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần.");

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
        
        // Xử lý đăng xuất tài khoản
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            // Xóa phiên đăng nhập của người dùng
            await _signInManager.SignOutAsync();

            // Chuyển về trang chủ
            return RedirectToAction("Index", "Home");
        }


    }
}
