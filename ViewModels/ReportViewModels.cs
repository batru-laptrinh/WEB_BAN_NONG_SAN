namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class ReportsViewModel
{
    public bool IsDataAvailable { get; init; } = true;
    public int ProductCount { get; init; }
    public int CustomerCount { get; init; }
    public int OrderCount { get; init; }
    public int PendingOrderCount { get; init; }
    public int CompletedOrderCount { get; init; }
    public decimal Revenue { get; init; }
    public DateTime CheckedAtUtc { get; init; }
    public IReadOnlyList<RevenuePeriod> DailyRevenue { get; init; } = [];
    public IReadOnlyList<RevenuePeriod> MonthlyRevenue { get; init; } = [];
    public IReadOnlyList<TopSellingProduct> TopProducts { get; init; } = [];
}

public sealed record RevenuePeriod(string Label, decimal Revenue, int OrderCount);
public sealed record TopSellingProduct(int ProductId, string ProductName, string Unit, long Quantity, decimal GrossValue);
