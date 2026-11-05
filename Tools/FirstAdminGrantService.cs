using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.Tools;

public sealed class FirstAdminGrantService(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    ApplicationDbContext db,
    IHostEnvironment environment)
{
    private const string AdminRole = "Admin";

    public async Task<int> GrantAsync(
        string email,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        // Defense in depth: enforce the gate even if called without the CLI wrapper.
        if (!environment.IsDevelopment())
        {
            await output.WriteLineAsync("Từ chối: công cụ chỉ được chạy trong Development.");
            return 3;
        }

        if (!FirstAdminCommand.IsValidEmail(email))
        {
            await output.WriteLineAsync("Cần email hợp lệ của tài khoản đã đăng ký.");
            return 2;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roles.RoleExistsAsync(AdminRole))
            {
                await output.WriteLineAsync("Vai trò Admin chưa tồn tại. Công cụ không tự tạo vai trò hoặc schema.");
                return 4;
            }

            var user = await users.FindByEmailAsync(email.Trim());
            if (user == null || !FirstAdminCommand.IsValidEmail(user.Email))
            {
                await output.WriteLineAsync("Không tìm thấy tài khoản hợp lệ. Hãy đăng ký tài khoản quản trị riêng trước.");
                return 4;
            }

            if (await users.IsInRoleAsync(user, AdminRole))
            {
                await output.WriteLineAsync($"Tài khoản {user.Email} đã có quyền Admin; không thay đổi.");
                return 0;
            }

            if ((await users.GetUsersInRoleAsync(AdminRole)).Count != 0)
            {
                await output.WriteLineAsync("Đã có tài khoản Admin khác. Công cụ Admin đầu tiên không cấp thêm quyền.");
                return 4;
            }

            if (await users.IsLockedOutAsync(user))
            {
                await output.WriteLineAsync("Tài khoản đích đang bị khóa. Không cấp quyền cho tài khoản bị khóa.");
                return 4;
            }

            var confirmedEmail = user.Email!;
            await output.WriteLineAsync($"Tài khoản sẽ nhận thêm quyền Admin: {confirmedEmail}");
            await output.WriteLineAsync("Quyền Customer hiện có sẽ được giữ nguyên. Không cần nhập mật khẩu.");
            await output.WriteLineAsync($"Để xác nhận, nhập chính xác: CAP ADMIN {confirmedEmail}");
            var confirmation = await input.ReadLineAsync(cancellationToken);
            if (!string.Equals(confirmation, $"CAP ADMIN {confirmedEmail}", StringComparison.Ordinal))
            {
                await output.WriteLineAsync("Đã hủy; chưa thay đổi quyền tài khoản.");
                return 5;
            }

            // Never hold a transaction while waiting for human input. Recheck under
            // Serializable so two concurrent first-admin attempts cannot both succeed.
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await db.Entry(user).ReloadAsync(cancellationToken);
            if (db.Entry(user).State == EntityState.Detached
                || !string.Equals(user.Email, confirmedEmail, StringComparison.Ordinal)
                || !await roles.RoleExistsAsync(AdminRole)
                || await users.IsLockedOutAsync(user))
            {
                await output.WriteLineAsync("Thông tin hoặc trạng thái tài khoản/vai trò đã thay đổi. Hãy kiểm tra lại.");
                return 4;
            }

            if (await users.IsInRoleAsync(user, AdminRole))
            {
                await output.WriteLineAsync($"Tài khoản {confirmedEmail} đã có quyền Admin; không cấp trùng.");
                return 0;
            }

            if ((await users.GetUsersInRoleAsync(AdminRole)).Count != 0)
            {
                await output.WriteLineAsync("Đã có tài khoản Admin khác trong lúc chờ xác nhận. Không cấp thêm quyền.");
                return 4;
            }

            var result = await users.AddToRoleAsync(user, AdminRole);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                await output.WriteLineAsync("Cấp quyền thất bại; giao dịch đã hủy, tài khoản và các quyền cũ được giữ nguyên.");
                return 1;
            }

            await transaction.CommitAsync(cancellationToken);
            await output.WriteLineAsync($"Đã cấp thêm quyền Admin cho {confirmedEmail}.");
            await output.WriteLineAsync("Hãy đăng xuất rồi đăng nhập lại để cookie nhận quyền mới.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("Thao tác đã dừng. Kiểm tra lại vai trò hiện tại trước khi chạy lại.");
            return 5;
        }
        catch (Exception)
        {
            // A lost connection during commit can have an uncertain outcome; do not
            // claim rollback or print exception/SQL/token/connection-string contents.
            await output.WriteLineAsync("Không xác nhận được kết quả cấp quyền. Kiểm tra kết nối và vai trò hiện tại trước khi chạy lại.");
            return 1;
        }
    }
}
