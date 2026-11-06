using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class CatalogAdminCategoryForm
{
    [Required(ErrorMessage = "Vui lòng nhập tên danh mục.")]
    [StringLength(100)]
    [Display(Name = "Tên danh mục")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Đang sử dụng")]
    public bool IsActive { get; set; } = true;

    [StringLength(32)]
    public string? Version { get; set; }

    [BindNever]
    public int Id { get; set; }
}

public sealed class CatalogAdminProductForm
{
    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(150)]
    [Display(Name = "Tên sản phẩm")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mô tả sản phẩm.")]
    [StringLength(4000)]
    [Display(Name = "Mô tả")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập đơn vị bán.")]
    [StringLength(30)]
    [Display(Name = "Đơn vị tính")]
    public string Unit { get; set; } = "kg";

    [Range(typeof(decimal), "0.01", "1000000000", ErrorMessage = "Giá phải từ 0,01 đến 1.000.000.000 đồng.")]
    [Display(Name = "Giá bán (đồng)")]
    public decimal Price { get; set; }

    [Range(0, 1000000, ErrorMessage = "Tồn kho phải từ 0 đến 1.000.000 đơn vị.")]
    [Display(Name = "Tồn kho")]
    public int Stock { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    [Display(Name = "Danh mục")]
    public int CategoryId { get; set; }

    [Display(Name = "Đang bán")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Sản phẩm nổi bật")]
    public bool IsFeatured { get; set; }

    [Display(Name = "Ảnh sản phẩm (PNG, tối đa 2 MB)")]
    public IFormFile? Image { get; set; }

    [StringLength(32)]
    public string? Version { get; set; }

    [BindNever]
    public int Id { get; set; }

    [BindNever]
    public string? CurrentImagePath { get; set; }

    [BindNever]
    public List<SelectListItem> Categories { get; set; } = [];
}

public sealed class CatalogAdminCategoryList
{
    public IReadOnlyList<Category> Items { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalCount { get; init; }
    public bool IsDataAvailable { get; init; } = true;
}

public sealed class CatalogAdminProductList
{
    public IReadOnlyList<Product> Items { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalCount { get; init; }
    public bool IsDataAvailable { get; init; } = true;
}
