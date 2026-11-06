namespace WEB_BAN_NONG_SAN.ViewModels;

public enum AdminUserLockoutState { Active, Temporary, Manual }

public sealed class AdminUserDetailsViewModel
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public AdminUserLockoutState LockoutState { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsSelf { get; init; }
    public bool CanLock => !IsAdmin && !IsSelf && LockoutState != AdminUserLockoutState.Manual;
    public bool CanUnlock => !IsAdmin && !IsSelf && LockoutState != AdminUserLockoutState.Active;
    public string StatusText => LockoutState switch
    {
        AdminUserLockoutState.Manual => "Khóa thủ công",
        AdminUserLockoutState.Temporary => "Khóa tạm thời",
        _ => "Đang hoạt động"
    };
}
