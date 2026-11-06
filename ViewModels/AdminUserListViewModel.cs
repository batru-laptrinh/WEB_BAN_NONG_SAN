namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class AdminUserListViewModel
{
    public IReadOnlyList<AdminUserDetailsViewModel> Items { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize => 10;
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool IsDataAvailable { get; init; } = true;
}
