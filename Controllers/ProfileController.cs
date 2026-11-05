
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;
using WEB_BAN_NONG_SAN.ViewModels;

namespace WEB_BAN_NONG_SAN.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public ProfileController(
            UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // Hiển thị thông tin cá nhân
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            var model = new ProfileViewModel
            {
                FullName = user.FullName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber ?? string.Empty
            };

            return View(model);
        }

        // Xử lý cập nhật thông tin cá nhân
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(
            ProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            model.Email = user.Email ?? string.Empty;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            user.FullName = model.FullName.Trim();

            var newPhone = string.IsNullOrWhiteSpace(
                model.PhoneNumber)
                ? null
                : model.PhoneNumber.Trim();

            if (user.PhoneNumber != newPhone)
            {
                user.PhoneNumber = newPhone;
                user.PhoneNumberConfirmed = false;
            }

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] =
                    "Cập nhật thông tin cá nhân thành công!";

                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    "", error.Description);
            }

            return View(model);
        }
    }
}
