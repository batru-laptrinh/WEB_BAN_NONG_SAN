using System.ComponentModel.DataAnnotations;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class CartViewModel
{
    public IReadOnlyList<CartLineViewModel> Items { get; init; } = [];
    public decimal Subtotal { get; init; }
    public bool IsAvailable { get; init; } = true;
    public bool CanCheckout => IsAvailable && Items.Count > 0 && Items.All(x => x.IsAvailable);
}

public sealed class CartLineViewModel
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public string? ImagePath { get; init; }
    public string Unit { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public int Stock { get; init; }
    public decimal LineTotal => UnitPrice * Quantity;
    public bool IsAvailable { get; init; }
    public string MutationToken { get; init; } = "";
}

public sealed class CartAddInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }
    [Range(1, 1000, ErrorMessage = "Số lượng từ 1 đến 1.000.")]
    public int Quantity { get; set; } = 1;
}

public sealed class CartUpdateInput
{
    [Range(1, 1000, ErrorMessage = "Số lượng từ 1 đến 1.000.")]
    public int Quantity { get; set; }
    [Required]
    public string MutationToken { get; set; } = "";
}
