namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class AddressIndexViewModel
{
    public IReadOnlyList<AddressItemViewModel> Items { get; init; } = [];
    public bool IsDataAvailable { get; init; } = true;
}

public sealed class AddressItemViewModel
{
    public int Id { get; init; }
    public string RecipientName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Province { get; init; } = string.Empty;
    public string Ward { get; init; } = string.Empty;
    public string? District { get; init; }
    public string AddressLine { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? DeliveryNote { get; init; }
    public bool IsDefault { get; init; }
    public string DefaultToken { get; init; } = string.Empty;
}

public sealed class AddressDeleteViewModel
{
    public required AddressItemViewModel Address { get; init; }
    public required string Confirmation { get; init; }
}
