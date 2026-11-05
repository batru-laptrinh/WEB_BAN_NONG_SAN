using Microsoft.AspNetCore.Mvc;

namespace WEB_BAN_NONG_SAN.Controllers
{
    public class ProductsController : Controller
    {
        // Danh sách Danh mục nông sản
        public static readonly List<CategoryItem> Categories = new()
        {
            new CategoryItem { Id = 1, Name = "Rau củ sạch", Icon = "bi-Flower1" },
            new CategoryItem { Id = 2, Name = "Trái cây tươi", Icon = "bi-apple" },
            new CategoryItem { Id = 3, Name = "Gạo & Ngũ cốc", Icon = "bi-basket2-fill" },
            new CategoryItem { Id = 4, Name = "Nấm & Thảo mộc", Icon = "bi-tree-fill" },
            new CategoryItem { Id = 5, Name = "Đặc sản vùng miền", Icon = "bi-award-fill" }
        };

        // Danh sách 12 Sản phẩm nông sản mẫu
        public static readonly List<ProductItem> Products = new()
        {
            new ProductItem { Id = 1, Name = "Cà chua Beef Đà Lạt VietGAP", CategoryId = 1, CategoryName = "Rau củ sạch", Price = 35000, Unit = "1 kg", Origin = "Đà Lạt, Lâm Đồng", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1592924357228-91a4daadcfea?auto=format&fit=crop&w=600&q=80", Description = "Cà chua Beef quả to, thịt dày, ít hạt, giàu vitamin C và Lycopene, canh tác theo tiêu chuẩn nhà kính VietGAP." },
            new ProductItem { Id = 2, Name = "Bông cải xanh hữu cơ", CategoryId = 1, CategoryName = "Rau củ sạch", Price = 42000, Unit = "500g", Origin = "Đơn Dương, Lâm Đồng", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1459411621453-7b03977f4bfc?auto=format&fit=crop&w=600&q=80", Description = "Bông cải xanh giòn ngọt tự nhiên, thu hoạch sáng sớm, không sử dụng thuốc bảo vệ thực vật hóa học." },
            new ProductItem { Id = 3, Name = "Cam Vinh lòng vàng loại 1", CategoryId = 2, CategoryName = "Trái cây tươi", Price = 65000, Unit = "1 kg", Origin = "Quỳ Hợp, Nghệ An", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1547514701-42782101795e?auto=format&fit=crop&w=600&q=80", Description = "Cam Vinh chính gốc Nghệ An vỏ mỏng, mọng nước, vị ngọt thanh thơm mát đặc trưng." },
            new ProductItem { Id = 4, Name = "Dâu tây Hana Đà Lạt", CategoryId = 2, CategoryName = "Trái cây tươi", Price = 145000, Unit = "Hộp 500g", Origin = "Đà Lạt, Lâm Đồng", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1464965911861-746a04b4bca6?auto=format&fit=crop&w=600&q=80", Description = "Dâu tây giống Nhật Hana quả căng mọng, độ ngọt cao, hương thơm dịu nhẹ, hái tươi tại vườn." },
            new ProductItem { Id = 5, Name = "Gạo ST25 Ông Cua Chính Hãng", CategoryId = 3, CategoryName = "Gạo & Ngũ cốc", Price = 195000, Unit = "Túi 5kg", Origin = "Sóc Trăng", IsNew = false, ImageUrl = "https://images.unsplash.com/photo-1586201375761-83865001e31c?auto=format&fit=crop&w=600&q=80", Description = "Gạo ngon nhất thế giới ST25 hạt dài trắng trong, cơm dẻo mềm, thơm mùi lá dứa tự nhiên." },
            new ProductItem { Id = 6, Name = "Khoai lang mật nướng Đà Lạt", CategoryId = 1, CategoryName = "Rau củ sạch", Price = 38000, Unit = "1 kg", Origin = "Lâm Đồng", IsNew = false, ImageUrl = "https://images.unsplash.com/photo-1596097635121-14b63b7a0c19?auto=format&fit=crop&w=600&q=80", Description = "Khoai lang mật tươm mật ngọt lịm khi nướng hoặc hấp, giàu chất xơ tốt cho sức khỏe." },
            new ProductItem { Id = 7, Name = "Bơ sáp 034 dẻo béo", CategoryId = 2, CategoryName = "Trái cây tươi", Price = 75000, Unit = "1 kg", Origin = "Bảo Lộc, Lâm Đồng", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1523049673857-eb18f1d7b578?auto=format&fit=crop&w=600&q=80", Description = "Bơ 034 quả thuôn dài, hạt nhỏ, cơm vàng ươm dẻo quánh, hàm lượng dinh dưỡng cao." },
            new ProductItem { Id = 8, Name = "Nấm đùi gà tươi hữu cơ", CategoryId = 4, CategoryName = "Nấm & Thảo mộc", Price = 55000, Unit = "Khay 500g", Origin = "Long Khánh, Đồng Nai", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1504674900247-0877df9cc836?auto=format&fit=crop&w=600&q=80", Description = "Nấm đùi gà thân trắng chắc, vị ngọt đậm đà, thích hợp xào, nướng hoặc nấu lẩu." },
            new ProductItem { Id = 9, Name = "Ớt chuông ba màu Đà Lạt", CategoryId = 1, CategoryName = "Rau củ sạch", Price = 48000, Unit = "500g", Origin = "Đà Lạt, Lâm Đồng", IsNew = false, ImageUrl = "https://images.unsplash.com/photo-1563565375-f3fdfdbefa83?auto=format&fit=crop&w=600&q=80", Description = "Ớt chuông giòn ngọt, nhiều màu sắc bắt mắt, cung cấp hàm lượng lớn vitamin A và C." },
            new ProductItem { Id = 10, Name = "Mật ong hoa nhãn nguyên chất", CategoryId = 5, CategoryName = "Đặc sản vùng miền", Price = 160000, Unit = "Chai 500ml", Origin = "Hưng Yên", IsNew = false, ImageUrl = "https://images.unsplash.com/photo-1587049352847-4a222e784d38?auto=format&fit=crop&w=600&q=80", Description = "Mật ong hoa nhãn thu hoạch tự nhiên, sánh đặc, thơm dịu, không pha đường." },
            new ProductItem { Id = 11, Name = "Xoài cát Hòa Lộc chín cây", CategoryId = 2, CategoryName = "Trái cây tươi", Price = 89000, Unit = "1 kg", Origin = "Tiền Giang", IsNew = true, ImageUrl = "https://images.unsplash.com/photo-1553279768-865429fa0078?auto=format&fit=crop&w=600&q=80", Description = "Xoài cát Hòa Lộc thịt vàng ươm, mịn màng, ít xơ, vị ngọt đậm và thơm nồng." },
            new ProductItem { Id = 12, Name = "Hạt điều rang củi Bình Phước", CategoryId = 5, CategoryName = "Đặc sản vùng miền", Price = 135000, Unit = "Hộp 500g", Origin = "Bình Phước", IsNew = false, ImageUrl = "https://images.unsplash.com/photo-1509358271058-acd22cc93898?auto=format&fit=crop&w=600&q=80", Description = "Hạt điều loại A rang củi thủ công giữ nguyên lớp vỏ lụa, giòn rụm và béo bùi." }
        };

        // Công việc 4, 6, 7, 8, 9: Danh sách + Tìm kiếm + Lọc + Sắp xếp + Phân trang
        public IActionResult Index(
            string? searchString,
            int? categoryId,
            decimal? minPrice,
            decimal? maxPrice,
            string? sortOrder,
            int page = 1)
        {
            int pageSize = 6; // Hiển thị 6 sản phẩm/trang để thấy rõ phân trang
            var query = Products.AsQueryable();

            // 6. Tìm kiếm sản phẩm theo tên
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase));
            }

            // 7. Lọc theo danh mục
            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            // 7. Lọc theo khoảng giá
            if (minPrice.HasValue)
            {
                query = query.Where(p => p.Price >= minPrice.Value);
            }
            if (maxPrice.HasValue)
            {
                query = query.Where(p => p.Price <= maxPrice.Value);
            }

            // 8. Sắp xếp theo giá, tên, sản phẩm mới
            query = sortOrder switch
            {
                "price_asc" => query.OrderBy(p => p.Price),
                "price_desc" => query.OrderByDescending(p => p.Price),
                "name_asc" => query.OrderBy(p => p.Name),
                "name_desc" => query.OrderByDescending(p => p.Name),
                _ => query.OrderByDescending(p => p.IsNew).ThenByDescending(p => p.Id) // Mặc định: Sản phẩm mới
            };

            // 9. Phân trang
            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = Math.Max(1, page);

            var pagedProducts = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.Categories = Categories;
            ViewBag.SearchString = searchString;
            ViewBag.CategoryId = categoryId;
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.SortOrder = sortOrder;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;

            return View(pagedProducts);
        }

        // Công việc 5: Trang chi tiết sản phẩm
        public IActionResult Details(int id)
        {
            var product = Products.FirstOrDefault(p => p.Id == id);
            if (product == null) return NotFound();

            ViewBag.RelatedProducts = Products
                .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id)
                .Take(4)
                .ToList();

            return View(product);
        }
    }

    public class CategoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "bi-basket";
    }

    public class ProductItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Origin { get; set; } = string.Empty;
        public bool IsNew { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}