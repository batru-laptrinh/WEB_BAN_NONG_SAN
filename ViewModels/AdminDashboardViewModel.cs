namespace WEB_BAN_NONG_SAN.ViewModels;

public sealed class AdminDashboardViewModel
{
    public int? CustomerCount { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public bool IsDataAvailable => CustomerCount.HasValue;
}
