using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class CouponFormViewModel : IValidatableObject
{
    private string code = "";
    [Required(ErrorMessage = "Vui lòng nhập mã giảm giá.")]
    [RegularExpression("^[A-Z0-9][A-Z0-9_-]{2,49}$", ErrorMessage = "Mã gồm 3–50 chữ Latin, số, dấu gạch ngang hoặc gạch dưới.")]
    [Display(Name = "Mã giảm giá")]
    public string Code { get => code; set => code = (value ?? "").Trim().ToUpperInvariant(); }

    [EnumDataType(typeof(CouponKind)), Display(Name = "Hình thức giảm")]
    public CouponKind Kind { get; set; }
    [Range(typeof(decimal), "0.01", "1000000000"), Display(Name = "Giá trị giảm")]
    public decimal Value { get; set; }
    [Range(typeof(decimal), "0", "1000000000"), Display(Name = "Tiền hàng tối thiểu (đồng)")]
    public decimal MinimumSubtotal { get; set; }
    [Required, DataType(DataType.DateTime), Display(Name = "Bắt đầu (UTC+7)")]
    public DateTime? StartsAtLocal { get; set; }
    [Required, DataType(DataType.DateTime), Display(Name = "Kết thúc (UTC+7)")]
    public DateTime? EndsAtLocal { get; set; }
    [Range(1, 1000000), Display(Name = "Tổng lượt được sử dụng")]
    public int MaxUses { get; set; } = 1;
    [Display(Name = "Đang bật")]
    public bool IsActive { get; set; } = true;
    [StringLength(32)] public string? Version { get; set; }
    [BindNever] public int Id { get; set; }
    [BindNever] public int UsedCount { get; set; }
    [BindNever] public bool HasBeenUsed { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Kind == CouponKind.Percentage && Value > 100m)
            yield return new("Phần trăm giảm không được lớn hơn 100.", [nameof(Value)]);
        if (decimal.Round(Value, 2) != Value || decimal.Round(MinimumSubtotal, 2) != MinimumSubtotal)
            yield return new("Giá trị tiền/phần trăm chỉ nhận tối đa hai chữ số thập phân.", [nameof(Value), nameof(MinimumSubtotal)]);
        if (StartsAtLocal is { } start && EndsAtLocal is { } end && end <= start)
            yield return new("Thời gian kết thúc phải sau thời gian bắt đầu.", [nameof(EndsAtLocal)]);
        if (StartsAtLocal is { Year: < 2000 or > 2100 } || EndsAtLocal is { Year: < 2000 or > 2100 })
            yield return new("Thời gian phải nằm trong khoảng năm 2000–2100.", [nameof(StartsAtLocal), nameof(EndsAtLocal)]);
    }
}

public sealed class CouponListViewModel
{
    public IReadOnlyList<Coupon> Items { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int TotalCount { get; init; }
    public bool IsDataAvailable { get; init; } = true;
}
