namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class AdminDashboardViewModel
{
    public int? CustomerCount { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public bool IsDataAvailable => CustomerCount.HasValue;
    public int? ProductCount { get; init; }
    public int? OrderCount { get; init; }
    public int? PendingOrderCount { get; init; }
    public int? CompletedOrderCount { get; init; }
    public decimal? CompletedRevenue { get; init; }
}
