using System.ComponentModel.DataAnnotations;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class CatalogQuery
{
    [StringLength(100, ErrorMessage = "Từ khóa tối đa 100 ký tự.")]
    public string? Q { get; set; }
    public int? CategoryId { get; set; }
    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "Giá lọc phải từ 0 đến 1 tỷ đồng.")]
    public decimal? MinPrice { get; set; }
    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "Giá lọc phải từ 0 đến 1 tỷ đồng.")]
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record CatalogCategory(int Id, string Name);

public sealed class CatalogProductCard
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal Price { get; init; }
    public int Stock { get; init; }
    public string CategoryName { get; init; } = "";
    public string? ImagePath { get; init; }
}

public sealed class CatalogIndexViewModel
{
    public CatalogQuery Query { get; init; } = new();
    public IReadOnlyList<CatalogCategory> Categories { get; init; } = [];
    public IReadOnlyList<CatalogProductCard> Products { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageSize { get; init; } = 12;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
    public bool IsDataAvailable { get; init; } = true;
}

public sealed class CatalogDetailsViewModel
{
    public CatalogProductCard Product { get; init; } = new();
    public string Description { get; init; } = "";
    public IReadOnlyList<CatalogProductCard> RelatedProducts { get; init; } = [];
}

public sealed class CatalogHomeViewModel
{
    public IReadOnlyList<CatalogCategory> Categories { get; init; } = [];
    public IReadOnlyList<CatalogProductCard> FeaturedProducts { get; init; } = [];
    public IReadOnlyList<CatalogProductCard> NewProducts { get; init; } = [];
    public bool IsDataAvailable { get; init; } = true;
}
