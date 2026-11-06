namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class AdminUserConfirmationViewModel
{
    public required AdminUserDetailsViewModel User { get; init; }
    public required string Operation { get; init; }
    public required string Confirmation { get; init; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
}
