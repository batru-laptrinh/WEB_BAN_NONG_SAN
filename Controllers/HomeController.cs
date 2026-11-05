using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.Controllers
{
    public class HomeController : Controller
    {
        // Công việc 1 & 3: Trang chủ + Banner quảng cáo + Danh mục nổi bật
        public IActionResult Index()
        {
            ViewBag.Categories = ProductsController.Categories;
            var featuredProducts = ProductsController.Products
                .OrderByDescending(p => p.IsNew)
                .Take(8)
                .ToList();

            return View(featuredProducts);
        }

        // Công việc 11: Trang giới thiệu
        public IActionResult About()
        {
            return View();
        }

        // Công việc 11: Trang liên hệ
        [HttpGet]
        public IActionResult Contact()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Contact(string fullName, string email, string phone, string message)
        {
            TempData["SuccessMessage"] = $"Cảm ơn {fullName}! Nông Sản Xanh đã nhận được thông tin và sẽ liên hệ lại qua số {phone} sớm nhất.";
            return RedirectToAction(nameof(Contact));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}